package builtin

import (
	"context"
	"fmt"
	"io"
	"net/http"
	"os"

	"revit-cli/internal/abstractions"
)

// LlmsHandler fetches and displays the llms.txt API reference from the bridge.
type LlmsHandler struct {
	BaseURL string
	Client  *http.Client
}

func (h LlmsHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "llms",
		Description: "Show Revit API reference (llms.txt)",
		Usage:       "llms [--save <path>]",
		Category:    abstractions.CategorySystem,
		Examples:    []string{"revit-cli.exe llms", "revit-cli.exe llms --save llms.txt"},
	}
}

func (h LlmsHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	url := h.BaseURL + "/api/llms.txt"

	resp, err := h.Client.Get(url)
	if err != nil {
		printErr(fmt.Sprintf("Cannot fetch llms.txt from %s: %v", url, err))
		return 1
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		body, _ := io.ReadAll(resp.Body)
		printErr(fmt.Sprintf("Server returned %d: %s", resp.StatusCode, string(body)))
		return 1
	}

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		printErr(fmt.Sprintf("Error reading response: %v", err))
		return 1
	}

	// Check if --save flag is provided.
	savePath, hasSave := abstractions.FindArg(args, "--save")
	if hasSave && savePath != "" {
		if err := os.WriteFile(savePath, body, 0o644); err != nil {
			printErr(fmt.Sprintf("Error writing to %s: %v", savePath, err))
			return 1
		}
		fmt.Printf("Saved llms.txt to %s\n", savePath)
		return 0
	}

	// Print to stdout.
	fmt.Println(string(body))
	return 0
}
