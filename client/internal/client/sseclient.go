package client

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"strings"
	"time"

	"revit-cli/internal/client/auth"
)

// Process exit codes, shared by every execution path:
// 0 success, 1 usage error, 2 unreachable or auth rejected,
// 3 task timed out, 4 command ran and failed.
const (
	ExitSuccess     = 0
	ExitUsage       = 1
	ExitConnect     = 2
	ExitTimeout     = 3
	ExitCommandFail = 4
)

// defaultTimeoutSeconds is the per-command budget when --timeout is absent.
// The bridge default (timeout_seconds in cli_bridge_setting.json) is higher;
// this value keeps the historical client behavior.
const defaultTimeoutSeconds = 120

// bridgeTimeoutMessage is the fixed text the bridge returns when a task
// exceeds its timeout. Both sides own this string; keep them in sync.
const bridgeTimeoutMessage = "Revit API execution timed out."

// SseClient handles command execution via SSE stream with legacy polling
// fallback. Mirrors C# RevitCliClient.SseClient.
type SseClient struct {
	httpClient    *http.Client
	baseURL       string
	lastSseTaskID string

	// TimeoutSeconds is the per-command budget in seconds (default 120).
	TimeoutSeconds int
	// NoWait submits the command asynchronously and returns the task ID
	// immediately instead of waiting for the result.
	NoWait bool
	// RequestID is sent as X-Request-Id. The bridge deduplicates retries
	// that reuse the same value instead of running the command twice.
	RequestID string
}

// NewSseClient creates a new SSE client targeting the given base URL.
func NewSseClient(baseURL string) *SseClient {
	return &SseClient{
		httpClient:     &http.Client{Timeout: 0},
		baseURL:        baseURL,
		TimeoutSeconds: defaultTimeoutSeconds,
	}
}

func (c *SseClient) timeoutSeconds() int {
	if c.TimeoutSeconds > 0 {
		return c.TimeoutSeconds
	}
	return defaultTimeoutSeconds
}

// Execute sends a command to the bridge and streams the result via SSE.
// Returns the process exit code (see Exit* constants).
// extraTopLevel maps are merged into the JSON payload's top level, letting
// callers forward bridge-level fields such as dry_run from raw JSON input.
// Mirrors C# SseClient.ExecuteAsync.
func (c *SseClient) Execute(ctx context.Context, command string, parameters interface{}, extraTopLevel ...map[string]interface{}) int {
	exitCode, err := c.consumeSSEStream(ctx, command, parameters, extraTopLevel...)
	if err != nil {
		errResp := map[string]interface{}{
			"status":  "error",
			"message": fmt.Sprintf("Cannot connect to Revit CLI server at %s", c.baseURL),
			"detail":  err.Error(),
		}
		b, _ := json.MarshalIndent(errResp, "", "  ")
		fmt.Println(string(b))
		return ExitConnect
	}
	return exitCode
}

// consumeSSEStream returns (exitCode, error). A non-nil error indicates a
// connection-level failure; otherwise exitCode is the command result.
func (c *SseClient) consumeSSEStream(ctx context.Context, command string, parameters interface{}, extraTopLevel ...map[string]interface{}) (int, error) {
	payload := map[string]interface{}{
		"command":         command,
		"timeout_seconds": c.timeoutSeconds(),
	}
	if c.NoWait {
		payload["async"] = true
	}
	if parameters != nil {
		if pm, ok := parameters.(map[string]interface{}); ok {
			if dr, exists := pm["dry_run"]; exists {
				payload["dry_run"] = dr
				delete(pm, "dry_run")
			}
		}
		payload["parameters"] = parameters
	}
	for _, extra := range extraTopLevel {
		for k, v := range extra {
			payload[k] = v
		}
	}

	body, err := json.Marshal(payload)
	if err != nil {
		return ExitCommandFail, err
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, c.baseURL+"/api/execute", bytes.NewReader(body))
	if err != nil {
		return ExitCommandFail, err
	}
	req.Header.Set("Content-Type", "application/json")
	if !c.NoWait {
		req.Header.Set("Accept", "text/event-stream")
	}
	if c.RequestID != "" {
		req.Header.Set("X-Request-Id", c.RequestID)
	}
	auth.WithAuth(req, c.baseURL)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return ExitConnect, err
	}
	defer resp.Body.Close()

	ct := resp.Header.Get("Content-Type")
	if !strings.HasPrefix(ct, "text/event-stream") {
		return c.handleLegacyResponse(resp)
	}

	return c.readSSEStream(ctx, command, resp.Body)
}

// handleLegacyResponse processes a non-SSE JSON response: an async submit
// receipt (--no-wait), a task-status snapshot (request-id dedup), a
// CommandResponse error, or any other legacy JSON. Mirrors C#
// SseClient.HandleLegacyResponseAsync.
func (c *SseClient) handleLegacyResponse(resp *http.Response) (int, error) {
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return ExitCommandFail, err
	}

	var obj map[string]interface{}
	if err := json.Unmarshal(body, &obj); err != nil {
		fmt.Println(string(body))
		return legacyHTTPExitCode(resp.StatusCode), nil
	}

	switch status, _ := obj["status"].(string); status {
	case "pending":
		if taskID, ok := obj["task_id"].(string); ok {
			if c.NoWait {
				fmt.Printf("task_id: %s\n", taskID)
				return ExitSuccess, nil
			}
			fmt.Fprintf(os.Stderr, "Task submitted: %s. Polling for result...\n", taskID)
			return c.pollTaskResult(context.Background(), taskID, c.timeoutSeconds(), 500), nil
		}
	case "completed", "failed", "timeout":
		if result, ok := obj["result"]; ok {
			fmt.Println(formatJSON(result))
		} else {
			fmt.Println(string(body))
		}
		switch status {
		case "completed":
			return ExitSuccess, nil
		case "timeout":
			return ExitTimeout, nil
		default:
			return ExitCommandFail, nil
		}
	case "error":
		fmt.Println(string(body))
		if resultMessage(obj) == bridgeTimeoutMessage {
			return ExitTimeout, nil
		}
		return legacyHTTPExitCode(resp.StatusCode), nil
	}

	fmt.Println(string(body))
	return legacyHTTPExitCode(resp.StatusCode), nil
}

// legacyHTTPExitCode maps an HTTP status to the exit-code contract:
// 401 means the bridge rejected the API key (or the client cannot reach it);
// other non-2xx statuses mean the command ran and failed.
func legacyHTTPExitCode(statusCode int) int {
	if statusCode == http.StatusUnauthorized {
		return ExitConnect
	}
	if statusCode >= 200 && statusCode < 300 {
		return ExitSuccess
	}
	return ExitCommandFail
}

// readSSEStream reads the SSE event stream and dispatches events.
// Mirrors C# SseClient.ReadSseStreamAsync. Returns the terminal exit code.
func (c *SseClient) readSSEStream(ctx context.Context, command string, body io.Reader) (int, error) {
	reader := bufio.NewReader(body)
	var currentEvent string
	var currentData strings.Builder
	lastProgress := -1
	heartbeatTimeout := 30 * time.Second

	for {
		select {
		case <-ctx.Done():
			return 1, ctx.Err()
		default:
		}

		line, err := readLineWithTimeout(ctx, reader, heartbeatTimeout)
		if err != nil {
			if err == io.EOF {
				log.Println("[SSE] Connection closed unexpectedly. Falling back to polling...")
			} else {
				log.Printf("[SSE] Stream I/O error: %v. Falling back to polling...", err)
			}
			return c.fallbackPollLastTask(command), nil
		}

		if line == "" {
			if currentEvent != "" && currentData.Len() > 0 {
				if exitCode, done := c.handleSSEEvent(currentEvent, currentData.String(), &lastProgress); done {
					return exitCode, nil
				}
			}
			currentEvent = ""
			currentData.Reset()
			continue
		}

		if strings.HasPrefix(line, ":") {
			continue
		}

		if strings.HasPrefix(line, "event: ") {
			currentEvent = strings.TrimPrefix(line, "event: ")
		} else if strings.HasPrefix(line, "data: ") {
			data := strings.TrimPrefix(line, "data: ")
			if currentData.Len() > 0 {
				currentData.WriteByte('\n')
			}
			currentData.WriteString(data)
		}
	}
}

// handleSSEEvent processes a single SSE event. Returns (exitCode, true) for
// terminal events, (0, false) to continue.
// Mirrors C# SseClient.HandleSseEvent.
func (c *SseClient) handleSSEEvent(eventName, data string, lastProgress *int) (int, bool) {
	var obj map[string]interface{}
	_ = json.Unmarshal([]byte(data), &obj)

	switch eventName {
	case "accepted":
		if taskID, ok := obj["task_id"].(string); ok {
			c.lastSseTaskID = taskID
			fmt.Fprintf(os.Stderr, "task_id: %s\n", taskID)
		}

	case "progress":
		if obj != nil {
			pct := -1
			if v, ok := obj["progress"]; ok {
				if f, ok := v.(float64); ok {
					pct = int(f)
				}
			}
			if pct != *lastProgress {
				*lastProgress = pct
				msg := ""
				if v, ok := obj["message"]; ok && v != nil {
					msg, _ = v.(string)
				}
				if msg != "" {
					fmt.Fprintf(os.Stdout, "\r  Progress: %d%% - %s    ", pct, msg)
				} else {
					fmt.Fprintf(os.Stdout, "\r  Progress: %d%%    ", pct)
				}
			}
		}

	case "completed":
		if result, ok := obj["result"]; ok {
			fmt.Fprintf(os.Stdout, "\n%s\n", formatJSON(result))
		}
		return ExitSuccess, true

	case "failed":
		if result, ok := obj["result"]; ok {
			fmt.Fprintf(os.Stderr, "\n%s\n", formatJSON(result))
		}
		if resultMessage(obj) == bridgeTimeoutMessage {
			return ExitTimeout, true
		}
		return ExitCommandFail, true

	case "heartbeat":
	}
	return 0, false
}

// resultMessage extracts the human-readable message from either a
// CommandResponse ({"message": ...}) or an SSE terminal event whose result
// carries the message ({"result": {"message": ...}}).
func resultMessage(obj map[string]interface{}) string {
	if m, ok := obj["message"].(string); ok {
		return m
	}
	if r, ok := obj["result"].(map[string]interface{}); ok {
		if m, ok := r["message"].(string); ok {
			return m
		}
	}
	return ""
}

// fallbackPollLastTask polls the last known task ID after an SSE failure.
// Mirrors C# SseClient.FallbackPollLastTaskAsync.
func (c *SseClient) fallbackPollLastTask(command string) int {
	if c.lastSseTaskID == "" {
		log.Printf("[SSE] No task_id for fallback. Command: %s", command)
		return ExitConnect
	}
	return c.pollTaskResult(context.Background(), c.lastSseTaskID, c.timeoutSeconds(), 500)
}

// pollTaskResult polls GET /api/task/{id} until completion or timeout.
// Respects context cancellation for clean shutdown.
// Mirrors C# SseClient.PollTaskResultAsync.
func (c *SseClient) pollTaskResult(ctx context.Context, taskID string, maxWaitSeconds, pollIntervalMs int) int {
	start := time.Now()
	maxWait := time.Duration(maxWaitSeconds) * time.Second
	interval := time.Duration(pollIntervalMs) * time.Millisecond

	for time.Since(start) < maxWait {
		select {
		case <-ctx.Done():
			return 1
		default:
		}

		select {
		case <-ctx.Done():
			return 1
		case <-time.After(interval):
		}

		req, err := http.NewRequestWithContext(ctx, http.MethodGet, c.baseURL+"/api/task/"+taskID, nil)
		if err != nil {
			continue
		}
		auth.WithAuth(req, c.baseURL)
		resp, err := c.httpClient.Do(req)
		if err != nil {
			continue
		}
		body, _ := io.ReadAll(resp.Body)
		resp.Body.Close()

		var taskObj map[string]interface{}
		if err := json.Unmarshal(body, &taskObj); err != nil {
			continue
		}

		status, _ := taskObj["status"].(string)
		if status == "completed" || status == "failed" || status == "timeout" {
			if result, ok := taskObj["result"]; ok {
				fmt.Println(formatJSON(result))
			} else {
				fmt.Println(string(body))
			}
			switch status {
			case "completed":
				return ExitSuccess
			case "timeout":
				return ExitTimeout
			default:
				return ExitCommandFail
			}
		}

		if progress, ok := taskObj["progress"]; ok && progress != nil {
			if f, ok := progress.(float64); ok {
				pct := int(f)
				if pct > 0 {
					msg := ""
					if v, ok := taskObj["progress_message"]; ok && v != nil {
						msg, _ = v.(string)
					}
					if msg != "" {
						fmt.Fprintf(os.Stdout, "\r  Progress: %d%% - %s    ", pct, msg)
					} else {
						fmt.Fprintf(os.Stdout, "\r  Progress: %d%%    ", pct)
					}
				}
			}
		}
	}

	fmt.Printf("\nTask %s timed out after %d seconds.\n", taskID, maxWaitSeconds)
	return ExitTimeout
}

func readLineWithTimeout(ctx context.Context, reader *bufio.Reader, timeout time.Duration) (string, error) {
	type result struct {
		line string
		err  error
	}
	ch := make(chan result, 1)

	readCtx, cancelRead := context.WithCancel(ctx)
	defer cancelRead()

	go func() {
		line, err := reader.ReadString('\n')
		select {
		case <-readCtx.Done():
			return
		case ch <- result{strings.TrimRight(line, "\r\n"), err}:
		}
	}()

	select {
	case res := <-ch:
		return res.line, res.err
	case <-time.After(timeout):
		return "", fmt.Errorf("heartbeat timeout (%s)", timeout)
	case <-ctx.Done():
		return "", ctx.Err()
	}
}

func formatJSON(v interface{}) string {
	b, err := json.MarshalIndent(v, "", "  ")
	if err != nil {
		return fmt.Sprintf("%v", v)
	}
	return string(b)
}
