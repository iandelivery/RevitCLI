// Package builtin implements the always-available CLI commands that do not
// require schema discovery. Mirrors C# RevitCliClient.BuiltIn.
package builtin

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"

	"revit-cli/internal/abstractions"
)

// httpGet performs a GET request and returns the pretty-printed JSON body.
// Used by status/health/task/commands/schema built-ins.
func httpGet(client *http.Client, url string) (string, int, error) {
	resp, err := client.Get(url)
	if err != nil {
		return "", 1, err
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return "", 1, err
	}

	// Try to pretty-print JSON; fall back to raw string.
	var obj interface{}
	if err := json.Unmarshal(body, &obj); err == nil {
		pretty, err := json.MarshalIndent(obj, "", "  ")
		if err == nil {
			return string(pretty), exitCodeFromStatus(resp.StatusCode), nil
		}
	}
	return string(body), exitCodeFromStatus(resp.StatusCode), nil
}

func exitCodeFromStatus(code int) int {
	if code >= 200 && code < 300 {
		return 0
	}
	return 1
}

func printErr(msg string) {
	fmt.Fprintln(os.Stderr, msg)
}

// --- PingHandler ---

// PingHandler tests the connection to Revit via the registered "ping" command.
// Mirrors C# RevitCliClient.BuiltIn.PingHandler.
type PingHandler struct{}

func (PingHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "ping",
		Description: "Test connection to Revit",
		Usage:       "ping [--json]",
		Category:    abstractions.CategorySystem,
		Examples:    []string{"revit-cli.exe ping", "revit-cli.exe ping --json"},
	}
}

func (PingHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	return send(ctx, "ping", nil)
}

// --- StatusHandler ---

// StatusHandler queries the server status via GET /api/status.
// Mirrors C# RevitCliClient.BuiltIn.StatusHandler.
type StatusHandler struct {
	BaseURL string
	Client  *http.Client
}

func (h StatusHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "status",
		Description: "Show service status",
		Usage:       "status [--json]",
		Category:    abstractions.CategorySystem,
		Examples:    []string{"revit-cli.exe status", "revit-cli.exe status --json"},
	}
}

func (h StatusHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	out, code, err := httpGet(h.Client, h.BaseURL+"/api/status")
	if err != nil {
		printErr(fmt.Sprintf("Cannot connect to Revit CLI server at %s: %v", h.BaseURL, err))
		return 1
	}
	fmt.Println(out)
	return code
}

// --- HealthHandler ---

// HealthHandler performs a health check via GET /api/health.
// Mirrors C# RevitCliClient.BuiltIn.HealthHandler.
type HealthHandler struct {
	BaseURL string
	Client  *http.Client
}

func (h HealthHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "health",
		Description: "Health check",
		Usage:       "health [--json]",
		Category:    abstractions.CategorySystem,
		Examples:    []string{"revit-cli.exe health", "revit-cli.exe health --json"},
	}
}

func (h HealthHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	out, code, err := httpGet(h.Client, h.BaseURL+"/api/health")
	if err != nil {
		printErr(fmt.Sprintf("Cannot connect to Revit CLI server at %s: %v", h.BaseURL, err))
		return 1
	}
	fmt.Println(out)
	return code
}

// --- TaskHandler ---

// TaskHandler queries task status. With a task ID, fetches a single task;
// without one, lists recent tasks. Mirrors C# RevitCliClient.BuiltIn.TaskHandler.
type TaskHandler struct {
	BaseURL string
	Client  *http.Client
}

func (h TaskHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "task",
		Description: "Query task status",
		Usage:       "task [-ti <id>] [--json]",
		Category:    abstractions.CategorySystem,
		Examples: []string{
			"revit-cli.exe task",
			"revit-cli.exe task -ti abc123",
			"revit-cli.exe task -ti abc123 --json",
		},
	}
}

func (h TaskHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	taskID, ok := abstractions.FindArg(args, "--task-id", "-ti")
	if ok && taskID != "" {
		out, code, err := httpGet(h.Client, h.BaseURL+"/api/task/"+taskID)
		if err != nil {
			printErr(fmt.Sprintf("Cannot fetch task %s: %v", taskID, err))
			return 1
		}
		fmt.Println(out)
		return code
	}
	// No ID — list all tasks.
	out, code, err := httpGet(h.Client, h.BaseURL+"/api/task")
	if err != nil {
		printErr(fmt.Sprintf("Cannot list tasks: %v", err))
		return 1
	}
	fmt.Println(out)
	return code
}

// --- RawHandler ---

// RawHandler sends a raw JSON command payload to the bridge.
// Mirrors C# RevitCliClient.BuiltIn.RawHandler.
type RawHandler struct{}

func (RawHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "raw",
		Description: "Send raw JSON command",
		Usage:       "raw -j <json>",
		Category:    abstractions.CategoryRaw,
		Examples:    []string{`revit-cli.exe raw -j "{\"command\":\"ping\"}"`},
	}
}

func (RawHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	jsonStr, ok := abstractions.FindArg(args, "--json", "-j")
	if !ok {
		printErr("Error: --json is required")
		return 1
	}

	var payload map[string]interface{}
	if err := json.Unmarshal([]byte(jsonStr), &payload); err != nil {
		printErr(fmt.Sprintf("Error: Invalid JSON: %v", err))
		return 1
	}

	command, _ := payload["command"].(string)
	if command == "" {
		printErr(`Error: JSON must contain a "command" field`)
		return 1
	}

	parameters := payload["parameters"]
	return send(ctx, command, parameters)
}
