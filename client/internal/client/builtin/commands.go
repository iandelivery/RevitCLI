package builtin

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"os"

	"revit-cli/internal/abstractions"
	"revit-cli/internal/client/discovery"
)

// CommandsHandler lists all commands known to the bridge (with local cache).
// Mirrors C# RevitCliClient.BuiltIn.CommandsCommand.
type CommandsHandler struct {
	BaseURL string
	Client  *http.Client
}

func (h CommandsHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "commands",
		Description: "List all commands (with cache)",
		Usage:       "commands [--refresh]",
		Category:    abstractions.CategorySystem,
		Examples:    []string{"revit-cli.exe commands", "revit-cli.exe commands --refresh"},
	}
}

func (h CommandsHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	forceRefresh := abstractions.HasFlag(args, "--refresh")
	fetcher := discovery.NewSchemaFetcher(h.BaseURL, h.Client)
	schema := fetcher.Fetch(forceRefresh)
	if schema == nil {
		printErr("Cannot fetch command schema from bridge.")
		return 1
	}

	fmt.Printf("Bridge version: %s\n", schema.Version)
	fmt.Printf("Commands (%d):\n", len(schema.Commands))
	for _, cmd := range schema.Commands {
		desc := cmd.Description
		if desc == "" {
			desc = "-"
		}
		fmt.Printf("  %-28s %s\n", cmd.Name, desc)
	}
	return 0
}

// SchemaHandler shows parameter details for a single command.
// Mirrors C# RevitCliClient.BuiltIn.SchemaCommand.
type SchemaHandler struct {
	BaseURL string
	Client  *http.Client
}

func (h SchemaHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "schema",
		Description: "Show command parameter details",
		Usage:       "schema <command>",
		Category:    abstractions.CategorySystem,
		Examples:    []string{"revit-cli.exe schema create_wall"},
	}
}

func (h SchemaHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	if len(args) == 0 {
		printErr("Error: command name required (e.g. schema create_wall)")
		return 1
	}
	name := args[0]

	// Try the per-command endpoint first.
	out, code, err := httpGet(h.Client, h.BaseURL+"/api/commands/"+name)
	if err == nil && code == 0 {
		fmt.Println(out)
		return 0
	}

	// Fallback: fetch full schema and filter locally.
	fetcher := discovery.NewSchemaFetcher(h.BaseURL, h.Client)
	schema := fetcher.Fetch(false)
	if schema == nil {
		printErr("Cannot fetch command schema from bridge.")
		return 1
	}
	for _, cmd := range schema.Commands {
		if cmd.Name == name {
			b, _ := json.MarshalIndent(cmd, "", "  ")
			fmt.Println(string(b))
			return 0
		}
	}
	fmt.Fprintf(os.Stderr, "Command '%s' not found in schema.\n", name)
	return 1
}
