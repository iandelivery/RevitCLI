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
)

// SseClient handles command execution via SSE stream with legacy polling
// fallback. Mirrors C# RevitCliClient.SseClient.
type SseClient struct {
	httpClient    *http.Client
	baseURL       string
	lastSseTaskID string
}

// NewSseClient creates a new SSE client targeting the given base URL.
func NewSseClient(baseURL string) *SseClient {
	return &SseClient{
		httpClient: &http.Client{Timeout: 0}, // no overall timeout; SSE is long-lived
		baseURL:    baseURL,
	}
}

// Execute sends a command to the bridge and streams the result via SSE.
// Returns the process exit code (0 success, 1 failure).
// Mirrors C# SseClient.ExecuteAsync.
func (c *SseClient) Execute(ctx context.Context, command string, parameters interface{}) int {
	exitCode, err := c.consumeSSEStream(ctx, command, parameters)
	if err != nil {
		errResp := map[string]interface{}{
			"status":  "error",
			"message": fmt.Sprintf("Cannot connect to Revit CLI server at %s", c.baseURL),
			"detail":  err.Error(),
		}
		b, _ := json.MarshalIndent(errResp, "", "  ")
		fmt.Println(string(b))
		return 1
	}
	return exitCode
}

// consumeSSEStream returns (exitCode, error). A non-nil error indicates a
// connection-level failure; otherwise exitCode is the command result.
func (c *SseClient) consumeSSEStream(ctx context.Context, command string, parameters interface{}) (int, error) {
	payload := map[string]interface{}{
		"command":         command,
		"timeout_seconds": 120,
	}
	if parameters != nil {
		payload["parameters"] = parameters
	}

	body, err := json.Marshal(payload)
	if err != nil {
		return 1, err
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, c.baseURL+"/api/execute", bytes.NewReader(body))
	if err != nil {
		return 1, err
	}
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Accept", "text/event-stream")

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return 1, err
	}
	defer resp.Body.Close()

	// If server doesn't support SSE, fall back to legacy JSON handling.
	ct := resp.Header.Get("Content-Type")
	if !strings.HasPrefix(ct, "text/event-stream") {
		return c.handleLegacyResponse(resp)
	}

	return c.readSSEStream(ctx, command, resp.Body)
}

// handleLegacyResponse processes a non-SSE JSON response, potentially polling
// for an async task. Mirrors C# SseClient.HandleLegacyResponseAsync.
func (c *SseClient) handleLegacyResponse(resp *http.Response) (int, error) {
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return 1, err
	}

	var obj map[string]interface{}
	if err := json.Unmarshal(body, &obj); err != nil {
		fmt.Println(string(body))
		return 1, nil
	}

	if status, _ := obj["status"].(string); status == "pending" {
		if taskID, ok := obj["task_id"].(string); ok {
			fmt.Printf("Task submitted: %s. Polling for result...\n", taskID)
			return c.pollTaskResult(context.Background(), taskID, 120, 500), nil
		}
	}

	fmt.Println(string(body))
	if resp.StatusCode >= 200 && resp.StatusCode < 300 {
		return 0, nil
	}
	return 1, nil
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

		// Read one line with a heartbeat timeout.
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
			// Blank line = end of event. Dispatch accumulated data.
			if currentEvent != "" && currentData.Len() > 0 {
				if exitCode, done := c.handleSSEEvent(currentEvent, currentData.String(), &lastProgress); done {
					return exitCode, nil
				}
			}
			currentEvent = ""
			currentData.Reset()
			continue
		}

		// Handle SSE comment lines (start with ':').
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
		return 0, true

	case "failed":
		if result, ok := obj["result"]; ok {
			fmt.Fprintf(os.Stderr, "\n%s\n", formatJSON(result))
		}
		return 1, true

	case "heartbeat":
		// no-op, timeout already reset by successful read
	}
	return 0, false
}

// fallbackPollLastTask polls the last known task ID after an SSE failure.
// Mirrors C# SseClient.FallbackPollLastTaskAsync.
func (c *SseClient) fallbackPollLastTask(command string) int {
	if c.lastSseTaskID == "" {
		log.Printf("[SSE] No task_id for fallback. Command: %s", command)
		return 1
	}
	return c.pollTaskResult(context.Background(), c.lastSseTaskID, 120, 500)
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

		// Use a select-based sleep so context cancellation is respected.
		select {
		case <-ctx.Done():
			return 1
		case <-time.After(interval):
		}

		resp, err := c.httpClient.Get(c.baseURL + "/api/task/" + taskID)
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
			if status == "completed" {
				return 0
			}
			return 1
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
	return 1
}

// readLineWithTimeout reads a single line from the reader, returning
// io.EOF or an error if no data arrives within the timeout.
// Uses context cancellation to prevent goroutine leaks.
func readLineWithTimeout(ctx context.Context, reader *bufio.Reader, timeout time.Duration) (string, error) {
	type result struct {
		line string
		err  error
	}
	ch := make(chan result, 1)

	// Create a cancellable context for the read goroutine.
	readCtx, cancelRead := context.WithCancel(ctx)
	defer cancelRead() // Ensure goroutine is cancelled when we return.

	go func() {
		line, err := reader.ReadString('\n')
		// Only send result if we haven't been cancelled.
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

// formatJSON pretty-prints a value as indented JSON, falling back to %v.
func formatJSON(v interface{}) string {
	b, err := json.MarshalIndent(v, "", "  ")
	if err != nil {
		return fmt.Sprintf("%v", v)
	}
	return string(b)
}
