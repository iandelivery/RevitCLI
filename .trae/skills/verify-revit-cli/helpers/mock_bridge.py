#!/usr/bin/env python3
"""Mock Revit CLI Bridge for verifying the revit-cli Go client.

Stands in for the Revit add-in at the HTTP boundary. Implements the routes,
payloads, SSE frames, ETag revalidation, request dedup, and units-contract
validation exactly as the real bridge (bridge/RevitCliBridge/CliBridge)
implements them, so the client can be driven end to end without Revit.

Python 3.8+, stdlib only.
"""

import argparse
import atexit
import hashlib
import json
import os
import socket
import sys
import threading
import time
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

REVIT_VERSION_DEFAULT = 2022
PORT_RANGE = range(25911, 25920)

UNITS = {
    "canonical": {"length": "mm", "area": "m2", "volume": "m3", "angle": "deg"},
    "allowed": {
        "length": ["mm", "cm", "m", "ft", "in"],
        "area": ["m2", "mm2", "ft2"],
        "volume": ["m3", "mm3", "ft3", "l"],
        "angle": ["deg", "rad"],
    },
}

COMMANDS = [
    {
        "name": "ping",
        "description": "Test connection to Revit",
        "category": "system",
        "supports_dry_run": False,
        "examples": ["revit-cli.exe ping"],
    },
    {
        "name": "create_wall",
        "description": "Create a wall between two points",
        "category": "modeling",
        "supports_dry_run": True,
        "parameters": [
            {"name": "start_x", "type": "double", "required": True,
             "description": "Start X", "short_flag": "sx", "unit_kind": "length"},
            {"name": "start_y", "type": "double", "required": True,
             "description": "Start Y", "short_flag": "sy", "unit_kind": "length"},
            {"name": "end_x", "type": "double", "required": True,
             "description": "End X", "short_flag": "ex", "unit_kind": "length"},
            {"name": "end_y", "type": "double", "required": True,
             "description": "End Y", "short_flag": "ey", "unit_kind": "length"},
            {"name": "level_id", "type": "int", "required": True,
             "description": "Level element id", "short_flag": "l"},
            {"name": "height", "type": "double", "required": False,
             "description": "Wall height", "default": 3000, "unit_kind": "length"},
        ],
        "examples": ["revit-cli.exe create_wall --start-x 0 --start-y 0 --end-x 5000 --end-y 0 -l 3001"],
    },
    {
        "name": "get_units",
        "description": "Return the units matrix (project/canonical/allowed)",
        "category": "query",
        "supports_dry_run": False,
        "examples": ["revit-cli.exe get_units"],
    },
    {
        "name": "elements",
        "description": "Query elements by category",
        "category": "query",
        "supports_dry_run": False,
        "parameters": [
            {"name": "category", "type": "string", "required": False,
             "description": "Built-in category", "short_flag": "c"},
        ],
        "examples": ["revit-cli.exe elements -c OST_Walls"],
    },
    {
        "name": "delete_elements",
        "description": "Delete elements by ids",
        "category": "modeling",
        "supports_dry_run": True,
        "parameters": [
            {"name": "element_ids", "type": "int[]", "required": True,
             "description": "Comma-separated ids", "short_flag": "ids"},
        ],
        "examples": ["revit-cli.exe delete_elements -ids 12345,12346"],
    },
]
COMMANDS_BY_NAME = {c["name"]: c for c in COMMANDS}

state = {
    "start_time": time.time(),
    "raw_enabled": True,
    "serve_documents": True,
    "fail_command": None,
    "slow_command": None,
    "tasks": {},
    "tasks_order": [],
    "by_request_id": {},
    "next_task": 1,
    "next_element_id": 4242,
    "log_lock": threading.Lock(),
    "log_file": None,
    "registry_path": None,
}


def log(line):
    if not state["log_file"]:
        return
    stamp = datetime.now(timezone.utc).strftime("%H:%M:%S.%f")[:-3]
    with state["log_lock"]:
        with open(state["log_file"], "a", encoding="utf-8") as f:
            f.write(f"{stamp} {line}\n")


def free_port(preferred=None):
    for p in ([preferred] if preferred else []) + list(PORT_RANGE):
        if p is None:
            continue
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
            try:
                s.bind(("127.0.0.1", p))
                return p
            except OSError:
                continue
    return None


def etag_for(obj):
    payload = json.dumps(obj, sort_keys=True, default=str).encode()
    return '"' + hashlib.sha256(payload).hexdigest()[:20] + '"'


def units_error(payload):
    units = payload.get("units")
    if units is None or units == "project":
        return None
    if not isinstance(units, dict):
        return "Invalid 'units': expected an object map or the string \"project\"."
    for kind, unit in units.items():
        if kind not in UNITS["allowed"]:
            allowed = ", ".join(UNITS["allowed"].keys())
            return f"Unknown unit kind '{kind}' in 'units'. Allowed kinds: {allowed}."
        if unit not in UNITS["allowed"][kind]:
            allowed = ", ".join(UNITS["allowed"][kind])
            return f"Unit '{unit}' is not allowed for kind '{kind}'. Allowed units: {allowed}."
    return None


def make_task(command, params, dry_run, result):
    task_id = f"mock-task-{state['next_task']}"
    state["next_task"] += 1
    state["next_element_id"] += 1
    task_status = "completed" if result["status"] == "success" else "failed"
    task = {
        "task_id": task_id,
        "command": command,
        "status": task_status,
        "progress": 100,
        "result": result,
        "created_at": datetime.now(timezone.utc).isoformat(),
    }
    state["tasks"][task_id] = task
    state["tasks_order"].append(task_id)
    if len(state["tasks_order"]) > 50:
        state["tasks"].pop(state["tasks_order"].pop(0), None)
    return task


def build_result(payload):
    command = payload.get("command", "")
    params = payload.get("parameters") or {}
    dry_run = bool(payload.get("dry_run", False))

    err = units_error(payload)
    if err:
        return {"status": "error", "message": err}

    if command not in COMMANDS_BY_NAME:
        return {"status": "error", "message": f"Unknown command: {command}"}

    if command == state["fail_command"]:
        return {"status": "error",
                "message": f"mock failure requested for {command}"}

    if command == state["slow_command"]:
        return {"status": "error", "message": "Revit API execution timed out."}

    result = {
        "status": "success",
        "command": command,
        "parameters": params,
        "dry_run": dry_run,
    }
    if payload.get("units") is not None:
        result["units"] = UNITS["canonical"] if payload.get("units") == "project" else payload["units"]
    if command == "get_units":
        result["data"] = {
            "project": dict(UNITS["canonical"]),
            "canonical": dict(UNITS["canonical"]),
            "allowed": {k: list(v) for k, v in UNITS["allowed"].items()},
        }
    elif not dry_run:
        result["element_id"] = state["next_element_id"]
    return result


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        pass

    def _json(self, obj, code=200, headers=None):
        body = json.dumps(obj, indent=2).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(body)

    def _text(self, text, code=200):
        body = text.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _body(self):
        length = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(length) if length else b""

    def _request_id(self):
        return self.headers.get("X-Request-Id", "-")

    def do_GET(self):
        rid = self._request_id()
        path = self.path.split("?", 1)[0]

        if path == "/api/health":
            self._json({"status": "ok", "timestamp": datetime.now(timezone.utc).isoformat()})
        elif path == "/api/identity":
            self._json({"version": "mock", "pid": os.getpid(),
                        "port": self.server.server_address[1],
                        "hostname": "localhost", "commands_count": len(COMMANDS)})
        elif path == "/api/auth/status":
            self._json({"auth_enabled": False, "scheme": "Bearer",
                        "message": "mock bridge does not enforce auth"})
        elif path == "/api/status":
            self._json({"server_running": True, "total_tasks": len(state["tasks"]),
                        "pending_tasks": 0, "running_tasks": 0, "queue_size": 0})
        elif path == "/api/documents":
            if not state["serve_documents"]:
                self._json({"error": "not found"}, code=404)
                log(f"GET {path} 404 rid={rid}")
                return
            self._json({"documents": [{"title": "MockModel", "path": "D:\\models\\MockModel.rvt", "is_active": True}],
                        "active_path": "D:\\models\\MockModel.rvt"})
        elif path == "/api/llms.txt":
            self._text("# Mock Revit API reference\nmock llms.txt\n")
        elif path == "/api/commands":
            self._schema()
        elif path.startswith("/api/commands/"):
            self._command_def(path[len("/api/commands/"):])
        elif path == "/api/catalog":
            self._json({"catalog_version": "1", "command_count": len(COMMANDS),
                        "commands": [{"name": c["name"], "category": c.get("category", ""),
                                      "summary": c.get("description", "")} for c in COMMANDS]})
        elif path == "/api/task":
            self._json({"tasks": [state["tasks"][t] for t in state["tasks_order"] if t in state["tasks"]],
                        "count": len(state["tasks"])})
        elif path.startswith("/api/task/"):
            task = state["tasks"].get(path[len("/api/task/"):])
            if task is None:
                self._json({"error": "task not found; tasks are pruned after retention"}, code=404)
            else:
                self._json(task)
        elif path == "/api/raw-mode":
            self._json({"allow_raw_execution": state["raw_enabled"]})
        else:
            self._json({"error": f"not found: {path}"}, code=404)
        log(f"GET {path} rid={rid}")

    def _schema(self):
        rid = self._request_id()
        schema = {"version": "2.0.0",
                  "server_info": {"bridge_version": "mock-1", "host": "127.0.0.1",
                                  "port": self.server.server_address[1],
                                  "features": {"dry_run": True, "execute_raw": False}},
                  "commands": COMMANDS}
        body = json.dumps(schema).encode("utf-8")
        tag = etag_for(schema)
        inm = self.headers.get("If-None-Match", "").strip('"')
        if inm and inm == tag.strip('"'):
            self.send_response(304)
            self.send_header("ETag", tag)
            self.send_header("Content-Length", "0")
            self.end_headers()
            log(f"GET /api/commands 304 rid={rid}")
            return
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("ETag", tag)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)
        log(f"GET /api/commands 200 rid={rid}")

    def _command_def(self, name):
        rid = self._request_id()
        cmd = COMMANDS_BY_NAME.get(name)
        if cmd is None:
            self._json({"error": f"command not found: {name}"}, code=404)
            log(f"GET /api/commands/{name} 404 rid={rid}")
            return
        tag = etag_for(cmd)
        inm = self.headers.get("If-None-Match", "").strip('"')
        if inm and inm == tag.strip('"'):
            self.send_response(304)
            self.send_header("ETag", tag)
            self.send_header("Content-Length", "0")
            self.end_headers()
        else:
            body = json.dumps(cmd).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("ETag", tag)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        log(f"GET /api/commands/{name} {'304' if inm else '200'} rid={rid}")

    def do_POST(self):
        rid = self._request_id()
        path = self.path.split("?", 1)[0]
        raw = self._body()

        if path == "/api/execute":
            self._execute(raw, rid)
            return

        if path == "/api/raw-mode":
            try:
                payload = json.loads(raw.decode("utf-8") or "{}")
            except json.JSONDecodeError:
                self._json({"error": "invalid JSON"}, code=400)
                log(f"POST {path} 400 rid={rid}")
                return
            if not isinstance(payload.get("enabled"), bool):
                self._json({"error": "body must contain boolean 'enabled'"}, code=400)
                log(f"POST {path} 400 rid={rid}")
                return
            state["raw_enabled"] = payload["enabled"]
            self._json({"allow_raw_execution": state["raw_enabled"],
                        "message": f"raw execution {'enabled' if state['raw_enabled'] else 'disabled'} (mock: in-memory)"})
            log(f"POST {path} enabled={state['raw_enabled']} rid={rid}")
            return

        self._json({"error": f"not found: {path}"}, code=404)
        log(f"POST {path} 404 rid={rid}")

    def _execute(self, raw, rid):
        try:
            payload = json.loads(raw.decode("utf-8") or "{}")
        except json.JSONDecodeError:
            self._json({"error": "invalid JSON"}, code=400)
            log("POST /api/execute 400 rid=%s" % rid)
            return

        command = payload.get("command")
        if not command:
            self._json({"error": "missing 'command'"}, code=400)
            log("POST /api/execute 400 rid=%s" % rid)
            return

        params = payload.get("parameters") or {}
        log(f"POST /api/execute command={command} params={json.dumps(params)}"
            f" dry_run={bool(payload.get('dry_run', False))}"
            f" async={bool(payload.get('async', False))}"
            f" units={json.dumps(payload.get('units'))} rid={rid}")

        if rid != "-" and rid in state["by_request_id"]:
            prior = state["by_request_id"][rid]
            self._json(prior)
            log(f"request_dedup task={prior.get('task_id')} rid={rid}")
            return

        result = build_result(payload)
        task = make_task(command, params, bool(payload.get("dry_run", False)), result)
        if rid != "-":
            state["by_request_id"][rid] = {
                "task_id": task["task_id"], "command": command,
                "status": task["status"], "result": result,
                "progress": 100,
            }

        wants_sse = (self.headers.get("Accept", "").startswith("text/event-stream"))
        async_mode = bool(payload.get("async", False))

        if not wants_sse:
            if async_mode:
                self._json({"task_id": task["task_id"], "status": "pending",
                            "message": "Task submitted. Poll GET /api/task/{task_id}."})
            else:
                code = 200 if result["status"] == "success" else 500
                self._json({"task_id": task["task_id"], **result}, code=code)
            return

        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.send_header("Cache-Control", "no-cache")
        self.send_header("Connection", "close")
        self.end_headers()

        def event(name, obj):
            self.wfile.write(f"event: {name}\ndata: {json.dumps(obj)}\n\n".encode("utf-8"))
            self.wfile.flush()

        event("accepted", {"task_id": task["task_id"], "status": "pending", "command": command})
        event("progress", {"task_id": task["task_id"], "progress": 0, "message": "Execution started"})
        event("progress", {"task_id": task["task_id"], "progress": 50, "message": "executing on mock bridge"})
        final = "completed" if result["status"] == "success" else "failed"
        event(final, {"task_id": task["task_id"], "status": task["status"], "result": result})
        self.close_connection = True


class QuietServer(ThreadingHTTPServer):
    daemon_threads = True

    def handle_error(self, request, client_address):
        exc = sys.exc_info()[1]
        if isinstance(exc, (ConnectionResetError, ConnectionAbortedError, BrokenPipeError)):
            return
        super().handle_error(request, client_address)


def write_registry(dirpath, version, port):
    os.makedirs(dirpath, exist_ok=True)
    path = os.path.join(dirpath, f"revit-{version}-{os.getpid()}.json")
    info = {"pid": os.getpid(), "version": version, "port": port,
            "document": "MockModel.rvt",
            "started_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "hostname": socket.gethostname(), "commands_count": len(COMMANDS)}
    with open(path, "w", encoding="utf-8") as f:
        json.dump(info, f)
    state["registry_path"] = path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=None)
    ap.add_argument("--fail-command", default=None)
    ap.add_argument("--slow-command", default=None)
    ap.add_argument("--no-documents", action="store_true")
    ap.add_argument("--registry-dir", default=None)
    ap.add_argument("--revit-version", type=int, default=REVIT_VERSION_DEFAULT)
    ap.add_argument("--log-file", default=None)
    ap.add_argument("--pidfile", default=None)
    args = ap.parse_args()

    state["fail_command"] = args.fail_command
    state["slow_command"] = args.slow_command
    state["log_file"] = args.log_file
    state["serve_documents"] = not args.no_documents

    port = free_port(args.port)
    if port is None:
        print(f"mock_bridge: no free port in {PORT_RANGE[0]}-{PORT_RANGE[-1]}", file=sys.stderr)
        sys.exit(1)

    server = QuietServer(("127.0.0.1", port), Handler)

    if args.pidfile:
        os.makedirs(os.path.dirname(os.path.abspath(args.pidfile)), exist_ok=True)
        with open(args.pidfile, "w", encoding="utf-8") as f:
            f.write(str(os.getpid()))

    if args.registry_dir:
        write_registry(args.registry_dir, args.revit_version, port)

    def cleanup():
        if state["registry_path"] and os.path.exists(state["registry_path"]):
            try:
                os.remove(state["registry_path"])
            except OSError:
                pass
        if args.pidfile and os.path.exists(args.pidfile):
            try:
                os.remove(args.pidfile)
            except OSError:
                pass

    atexit.register(cleanup)

    print(f"mock bridge listening on http://127.0.0.1:{port} pid={os.getpid()}",
          flush=True)
    log(f"mock bridge listening on 127.0.0.1:{port} pid={os.getpid()}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
        cleanup()


if __name__ == "__main__":
    main()
