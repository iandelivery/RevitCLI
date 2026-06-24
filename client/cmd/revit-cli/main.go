// Command revit-cli is the entry point for the Go Revit CLI client.
// It parses arguments, registers built-in commands, lazily discovers command
// schemas from the bridge, and dispatches commands via SSE.
// Mirrors C# RevitCliClient.Program.
package main

import (
	"context"
	"fmt"
	"net/http"
	"os"
	"strconv"
	"strings"

	"revit-cli/internal/abstractions"
	"revit-cli/internal/client"
	"revit-cli/internal/client/builtin"
	"revit-cli/internal/client/discovery"
	"revit-cli/internal/instance"
)

// Version is set at build time via -ldflags "-X main.Version=...".
var Version = "dev"

func main() {
	os.Exit(run(os.Args[1:]))
}

func run(args []string) int {
	if len(args) == 0 || args[0] == "--help" || args[0] == "-h" {
		fmt.Print(printHelp(nil, ""))
		return 0
	}

	if args[0] == "--version" || args[0] == "-V" {
		fmt.Printf("revit-cli %s\n", Version)
		return 0
	}

	baseURL, cmdIndex := parseArgs(args)
	if cmdIndex >= len(args) {
		fmt.Print(printHelp(nil, baseURL))
		return 0
	}

	registry := client.NewCommandRegistry()
	httpClient := &http.Client{Timeout: 0}

	// 1. Register built-in commands (always available, no server needed).
	registerBuiltIns(registry, baseURL, httpClient)

	commandName := args[cmdIndex]
	sseClient := client.NewSseClient(baseURL)

	// sendCommand dispatches a command to the bridge via SSE. Output
	// post-processing (--jq/--fields/--fmt) is currently a stub; when
	// implemented it will wrap Execute to capture and transform stdout.
	sendCommand := abstractions.SendCommandFunc(func(ctx context.Context, cmd string, params interface{}) int {
		return sseClient.Execute(ctx, cmd, params)
	})

	ctx := context.Background()

	// 2. If the command is built-in, execute immediately (no discovery needed).
	if cmd, ok := registry.TryGetCommand(commandName); ok {
		return cmd.Handle(ctx, args[cmdIndex+1:], sendCommand)
	}

	// 3. Lazy discovery: fetch schema only when user invokes a non-built-in command.
	fetcher := discovery.NewSchemaFetcher(baseURL, httpClient)
	schema := fetcher.Fetch(false)
	if schema != nil {
		for _, def := range schema.Commands {
			// Skip dynamic commands that already have a built-in handler.
			// Built-ins take priority (e.g. "ping", "execute_raw").
			if _, exists := registry.TryGetCommand(def.Name); exists {
				continue
			}
			registry.Register(discovery.NewDynamicCommand(def))
			// Register aliases.
			for _, alias := range def.Aliases {
				registry.RegisterAlias(alias, def.Name)
			}
		}
	}

	// 4. Try again with discovered commands.
	if cmd, ok := registry.TryGetCommand(commandName); ok {
		return cmd.Handle(ctx, args[cmdIndex+1:], sendCommand)
	}

	fmt.Fprintf(os.Stderr, "Unknown command: %s\n", commandName)
	fmt.Fprintln(os.Stderr, "Run 'revit-cli.exe commands' to see available commands.")
	return 1
}

// parseArgs extracts the --url, --revit, --pid flags and returns the base URL
// and the index where the command name begins.
// Resolution order: --url > --pid > --revit > auto-discover > fallback.
func parseArgs(args []string) (string, int) {
	var explicitURL string
	var pidFlag int
	var revitFlag int
	cmdIndex := -1

	for i := 0; i < len(args); i++ {
		switch args[i] {
		case "--url":
			if i+1 < len(args) {
				explicitURL = strings.TrimRight(args[i+1], "/")
				i++
			}
		case "--pid":
			if i+1 < len(args) {
				v, err := strconv.Atoi(args[i+1])
				if err != nil || v <= 0 {
					fmt.Fprintf(os.Stderr, "Invalid --pid value: %s (expected positive integer)\n", args[i+1])
					os.Exit(1)
				}
				pidFlag = v
				i++
			}
		case "--revit":
			if i+1 < len(args) {
				if v, ok := instance.ParseVersion(args[i+1]); ok {
					revitFlag = v
				} else {
					fmt.Fprintf(os.Stderr, "Invalid --revit version: %s (expected e.g. 2022)\n", args[i+1])
					os.Exit(1)
				}
				i++
			}
		default:
			// First non-flag argument is the command.
			if !strings.HasPrefix(args[i], "-") && cmdIndex == -1 {
				cmdIndex = i
			}
		}
	}

	if cmdIndex == -1 {
		cmdIndex = len(args)
	}

	baseURL := instance.ResolveURL(explicitURL, pidFlag, revitFlag)
	return baseURL, cmdIndex
}

// registerBuiltIns registers the always-available commands.
// Mirrors C# Program.RegisterBuiltInCommands.
func registerBuiltIns(registry *client.CommandRegistry, baseURL string, httpClient *http.Client) {
	registry.Register(builtin.PingHandler{})
	registry.Register(builtin.StatusHandler{BaseURL: baseURL, Client: httpClient})
	registry.Register(builtin.HealthHandler{BaseURL: baseURL, Client: httpClient})
	registry.Register(builtin.TaskHandler{BaseURL: baseURL, Client: httpClient})
	registry.Register(builtin.RawHandler{})
	registry.Register(builtin.CommandsHandler{BaseURL: baseURL, Client: httpClient})
	registry.Register(builtin.SchemaHandler{BaseURL: baseURL, Client: httpClient})
	registry.Register(builtin.ExecuteRawHandler{})
	registry.Register(builtin.ListHandler{})
	registry.Register(builtin.LlmsHandler{BaseURL: baseURL, Client: httpClient})
	registry.Register(builtin.ConfigureHandler{})
}

// printHelp generates help text, optionally discovering commands for richer help.
// Mirrors C# Program.PrintHelpAsync.
func printHelp(registry *client.CommandRegistry, baseURL string) string {
	if registry == nil {
		registry = client.NewCommandRegistry()
		httpClient := &http.Client{Timeout: 0}
		registerBuiltIns(registry, baseURL, httpClient)

		// Try to discover commands for richer help.
		if baseURL != "" {
			fetcher := discovery.NewSchemaFetcher(baseURL, httpClient)
			schema := fetcher.Fetch(false)
			if schema != nil {
				for _, def := range schema.Commands {
					if _, exists := registry.TryGetCommand(def.Name); exists {
						continue
					}
					registry.Register(discovery.NewDynamicCommand(def))
					for _, alias := range def.Aliases {
						registry.RegisterAlias(alias, def.Name)
					}
				}
			}
		}
	}
	return client.Generate(registry)
}
