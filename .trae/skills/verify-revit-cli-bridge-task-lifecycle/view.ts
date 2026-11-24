const none = "none";

const statusLabel = (s) => ({ pending: "pending", running: "running", completed: "completed", failed: "failed", timeout: "timed out" })[s];
const payload = (p) => ({ ok: "the ok result", fail: "the error result", timeout: "the timeout error" })[p];
const ownPayload = (s) => ({ completed: "ok", failed: "fail", timeout: "timeout" })[s];

const revitLabel = (v) => {
  switch (v.revitPc) {
    case "queued": return "about to start the command";
    case "running": return "running the command";
    case "done": return `the command returned ${v.outcome === "ok" ? "ok" : "an error"}, about to record it`;
    case "statusSet":
    case "resultSet": return "writing the outcome";
    case "exit": return "done";
    default: return v.revitPc;
  }
};

const httpLabel = (p) => {
  switch (p) {
    case "waiting": return "waiting for the result or the timeout";
    case "woke": return "its wait is over, about to record it";
    case "statusSet":
    case "resultSet": return "writing 'timed out'";
    case "finalPending": return "about to send the final frame";
    case "exit": return "done";
    default: return p;
  }
};

const frames = (s) => Object.keys(s).sort((a, b) => a - b).map((k) => s[k]);

export const state = (v) => {
  let record = `Task: ${statusLabel(v.status)}`;
  if (v.result !== none && v.result !== ownPayload(v.status)) record += `, result: ${payload(v.result)}`;
  if (v.tcs !== none && v.tcs !== ownPayload(v.status)) record += `, handed to the client: ${payload(v.tcs)}`;
  const wire = frames(v.stream);
  return `${record}${wire.length ? `, wire: ${wire.join(" > ")}` : ""} | Revit: ${revitLabel(v)} | HTTP thread: ${httpLabel(v.httpPc)}`;
};

export const step = (action, from, to) => {
  switch (action) {
    case "RevitStart": return to.revitPc === "exit" ? "Revit reads the status: already ended, skips the command" : "Revit writes: running, sends 'progress 0'";
    case "RevitFinishes": return `the command returns ${to.outcome === "ok" ? "ok" : "an error"}`;
    case "RevitEnd": return to.revitPc === "exit" ? "Revit reads the status: already ended, keeps it" : `Revit writes: ${to.status}, sends '${to.outcome === "ok" ? "completed" : "failed"}'`;
    case "RevitResultWrite": return "Revit stores the result";
    case "RevitTcsWrite": return from.tcs !== none ? "Revit tries to hand over the result, the client already has one" : "Revit hands the result to the waiting client";
    case "HttpWakes": return from.tcs !== none ? "the result reaches the HTTP thread before the timeout" : "the timeout fires";
    case "HttpEnd": return to.httpPc === "finalPending" ? "the HTTP thread reads the status: already ended" : "the HTTP thread writes: timed out, sends 'failed'";
    case "HttpResultWrite": return "the HTTP thread stores the timeout error";
    case "HttpTcsWrite": return from.tcs !== none ? "the HTTP thread tries to hand over the timeout error, the client already has one" : "the HTTP thread hands the timeout error to the client";
    case "HttpFinalFrame": return `the HTTP thread sends the final frame '${to.status === "completed" ? "completed" : "failed"}'`;
    case "Done": return null;
    default: return action;
  }
};

export const group = (v) => v.status;
export const colors = { pending: "blue", running: "teal", completed: "purple", failed: "orange", timeout: "pink" };
export const title = "Revit CLI bridge task lifecycle: who ends a task, and what the client is told";
