package builtin

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"

	"revit-cli/internal/abstractions"
)

// RawModeHandler queries or toggles raw execution mode on the bridge.
//   - Without flags: shows current state (GET /api/raw-mode)
//   - With --enable or --disable: toggles the setting (POST /api/raw-mode)
type RawModeHandler struct {
	BaseURL string
	Client  *http.Client
}

func (h RawModeHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "raw-mode",
		Description: "Query or toggle raw execution mode on the bridge",
		Usage:       "raw-mode [--enable | --disable]",
		Category:    abstractions.CategoryRaw,
		Examples: []string{
			"revit-cli.exe raw-mode",
			"revit-cli.exe raw-mode --enable",
			"revit-cli.exe raw-mode --disable",
		},
	}
}

func (h RawModeHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	enable := abstractions.HasFlag(args, "--enable")
	disable := abstractions.HasFlag(args, "--disable")

	if enable && disable {
		printErr("Error: --enable and --disable are mutually exclusive")
		return 1
	}

	if !enable && !disable {
		// Query current state
		return h.query()
	}

	// Toggle state
	enabled := enable
	return h.set(enabled)
}

func (h RawModeHandler) query() int {
	out, code, err := httpGet(h.Client, h.BaseURL+"/api/raw-mode")
	if err != nil {
		printErr(fmt.Sprintf("Cannot query raw mode: %v", err))
		return 1
	}
	fmt.Println(out)
	return code
}

func (h RawModeHandler) set(enabled bool) int {
	body, _ := json.Marshal(map[string]bool{"enabled": enabled})
	resp, err := h.Client.Post(h.BaseURL+"/api/raw-mode", "application/json", bytes.NewReader(body))
	if err != nil {
		printErr(fmt.Sprintf("Cannot set raw mode: %v", err))
		return 1
	}
	defer resp.Body.Close()

	respBody, err := io.ReadAll(resp.Body)
	if err != nil {
		printErr(fmt.Sprintf("Cannot read response: %v", err))
		return 1
	}

	// Pretty-print JSON response
	var obj interface{}
	if err := json.Unmarshal(respBody, &obj); err == nil {
		pretty, err := json.MarshalIndent(obj, "", "  ")
		if err == nil {
			fmt.Println(string(pretty))
		} else {
			fmt.Println(string(respBody))
		}
	} else {
		fmt.Println(string(respBody))
	}

	return exitCodeFromStatus(resp.StatusCode)
}
