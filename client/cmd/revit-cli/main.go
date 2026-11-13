// Command revit-cli is the entry point for the Go Revit CLI client.
// It uses cobra for command dispatch and help generation, and lazily
// discovers command schemas from the bridge for dynamic commands.
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

	"github.com/spf13/cobra"
)

// Version is set at build time via -ldflags "-X main.Version=...".
var Version = "dev"

// Author is set at build time via -ldflags "-X main.Author=...".
var Author = "Ian Chan"

const banner = ".-./`)    ____    ,---.   .--.            _______   .---.  .---.    ____    ,---.   .--. \n" +
	"\\ .-.') .'  __ `. |    \\  |  |           /   __  \\  |   |  |_ _|  .'  __ `. |    \\  |  | \n" +
	"/ `-' \\/   '  \\  \\|  ,  \\ |  |          | ,_/  \\__) |   |  ( ' ) /   '  \\  \\|  ,  \\ |  | \n" +
	" `-'`\"`|___|  /  ||  |\\_ \\|  |        ,-./  )       |   '-(_{;}_)|___|  /  ||  |\\_ \\|  | \n" +
	" .---.    _.-`   ||  _( )_\\  |        \\  '_ '`)     |      (_,_)    _.-`   ||  _( )_\\  | \n" +
	" |   | .'   _    || (_ o _)  |         > (_)  )  __ | _ _--.   | .'   _    || (_ o _)  | \n" +
	" |   | |  _( )_  ||  (_,_)\\  |        (  .  .-'_/  )|( ' ) |   | |  _( )_  ||  (_,_)\\  | \n" +
	" |   | \\ (_ o _) /|  |    |  |         `-'`-'     / (_{;}_)|   | \\ (_ o _) /|  |    |  | \n" +
	" '---'  '.(_,_).' '--'    '--'           `._____.'  '(_,_) '---'  '.(_,_).' '--'    '--'"

var exitCode int

var (
	flagURL   string
	flagPID   int
	flagRevit int
)

func main() {
	builtin.ClientVersion = Version
	root := newRootCmd()
	if err := root.Execute(); err != nil && exitCode == 0 {
		os.Exit(1)
	}
	os.Exit(exitCode)
}

const skillHint = "AI agent integration: run 'revit-cli skill install' to register this tool with Claude Code / Cursor / Trae / CodeBuddy."

func newRootCmd() *cobra.Command {
	longDesc := fmt.Sprintf("%s\n\nRevit CLI Client (Go) v%s\nCommand-line tool for AI agents to drive Autodesk Revit.\nDeveloped by %s.\n\n%s",
		banner, Version, Author, skillHint)
	root := &cobra.Command{
		Use:                "revit-cli [flags] <command> [args]",
		Short:              "Command-line tool for AI agents to drive Autodesk Revit",
		Long:               longDesc,
		DisableFlagParsing: true,
		SilenceUsage:       true,
		SilenceErrors:      true,
		Args:               cobra.ArbitraryArgs,
		RunE: func(cmd *cobra.Command, args []string) error {
			for _, a := range args {
				if a == "--help" || a == "-h" {
					return cmd.Help()
				}
				if a == "--version" || a == "-V" {
					fmt.Println(banner)
					fmt.Printf("revit-cli %s\n", Version)
					fmt.Printf("Author: %s\n", Author)
					fmt.Println(skillHint)
					return nil
				}
			}
			if len(args) == 0 {
				return cmd.Help()
			}
			exitCode = dispatchDynamic(args)
			return nil
		},
	}

	root.PersistentFlags().StringVar(&flagURL, "url", "", "Revit CLI server address (default: auto-discover)")
	root.PersistentFlags().IntVar(&flagPID, "pid", 0, "Connect to a specific Revit instance by process ID")
	root.PersistentFlags().IntVar(&flagRevit, "revit", 0, "Connect to a specific Revit version (e.g. 2022)")

	registerBuiltinCmds(root)
	return root
}

func resolveBaseURL() string {
	args := os.Args[1:]
	var explicitURL string
	var pidFlag int
	var revitFlag int

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
		}
	}

	return instance.ResolveURL(explicitURL, pidFlag, revitFlag)
}

func makeSend(baseURL string) abstractions.SendCommandFunc {
	sseClient := client.NewSseClient(baseURL)
	return abstractions.SendCommandFunc(func(ctx context.Context, cmd string, params interface{}, extra ...map[string]interface{}) int {
		return sseClient.Execute(ctx, cmd, params, extra...)
	})
}

func dispatchDynamic(args []string) int {
	baseURL := resolveBaseURL()

	cmdName := ""
	var cmdArgs []string
	for i := 0; i < len(args); i++ {
		a := args[i]
		if a == "--url" || a == "--pid" || a == "--revit" {
			i++
			continue
		}
		if strings.HasPrefix(a, "--url=") || strings.HasPrefix(a, "--pid=") || strings.HasPrefix(a, "--revit=") {
			continue
		}
		if strings.HasPrefix(a, "-") {
			continue
		}
		cmdName = a
		cmdArgs = args[i+1:]
		break
	}
	if cmdName == "" {
		return 0
	}

	httpClient := &http.Client{Timeout: 0}
	fetcher := discovery.NewSchemaFetcher(baseURL, httpClient)

	if def := fetcher.FetchCommand(cmdName); def != nil {
		dynCmd := discovery.NewDynamicCommand(*def)
		send := makeSend(baseURL)
		return dynCmd.Handle(context.Background(), cmdArgs, send)
	}

	schema := fetcher.Fetch(false)
	if schema == nil {
		fmt.Fprintf(os.Stderr, "Unknown command: %s\n", cmdName)
		fmt.Fprintln(os.Stderr, "Run 'revit-cli commands' to see available commands.")
		return 1
	}

	for _, def := range schema.Commands {
		if def.Name != cmdName {
			matched := false
			for _, alias := range def.Aliases {
				if alias == cmdName {
					matched = true
					break
				}
			}
			if !matched {
				continue
			}
		}
		dynCmd := discovery.NewDynamicCommand(def)
		send := makeSend(baseURL)
		return dynCmd.Handle(context.Background(), cmdArgs, send)
	}

	fmt.Fprintf(os.Stderr, "Unknown command: %s\n", cmdName)
	fmt.Fprintln(os.Stderr, "Run 'revit-cli commands' to see available commands.")
	return 1
}

func registerBuiltinCmds(root *cobra.Command) {
	root.AddCommand(newBuiltinCmd(
		"ping [--json]", "Test connection to Revit",
		[]string{"revit-cli ping", "revit-cli ping --json"},
		true,
		func(_ string, _ *http.Client) abstractions.CliCommand {
			return builtin.PingHandler{}
		},
	))

	root.AddCommand(newBuiltinCmd(
		"raw -j <json>", "Send raw JSON command",
		[]string{`revit-cli raw -j "{\"command\":\"ping\"}"`},
		true,
		func(_ string, _ *http.Client) abstractions.CliCommand {
			return builtin.RawHandler{}
		},
	))

	root.AddCommand(newBuiltinCmd(
		"execute_raw --code <code> | --file <path> [--lang csharp]",
		"Execute C# code on the bridge",
		[]string{
			`revit-cli execute_raw --lang csharp --code "return doc.Title;"`,
			`revit-cli execute_raw --file script.cs --lang csharp`,
		},
		true,
		func(_ string, _ *http.Client) abstractions.CliCommand {
			return builtin.ExecuteRawHandler{}
		},
	))

	root.AddCommand(newBuiltinCmd(
		"status [--json]", "Show service status",
		[]string{"revit-cli status", "revit-cli status --json"},
		true,
		func(baseURL string, httpClient *http.Client) abstractions.CliCommand {
			return builtin.StatusHandler{BaseURL: baseURL, Client: httpClient}
		},
	))

	root.AddCommand(newBuiltinCmd(
		"health [--json]", "Health check",
		[]string{"revit-cli health", "revit-cli health --json"},
		true,
		func(baseURL string, httpClient *http.Client) abstractions.CliCommand {
			return builtin.HealthHandler{BaseURL: baseURL, Client: httpClient}
		},
	))

	root.AddCommand(newBuiltinCmd(
		"task [-ti <id>] [--json]", "Query task status",
		[]string{"revit-cli task", "revit-cli task -ti abc123", "revit-cli task -ti abc123 --json"},
		true,
		func(baseURL string, httpClient *http.Client) abstractions.CliCommand {
			return builtin.TaskHandler{BaseURL: baseURL, Client: httpClient}
		},
	))

	root.AddCommand(newBuiltinCmd(
		"commands [--refresh]", "List all commands (with cache)",
		[]string{"revit-cli commands", "revit-cli commands --refresh"},
		true,
		func(baseURL string, httpClient *http.Client) abstractions.CliCommand {
			return builtin.CommandsHandler{BaseURL: baseURL, Client: httpClient}
		},
	))

	root.AddCommand(newBuiltinCmd(
		"catalog [--json]", "List all commands (compact index)",
		[]string{"revit-cli catalog", "revit-cli catalog --json"},
		true,
		func(baseURL string, httpClient *http.Client) abstractions.CliCommand {
			return builtin.CatalogHandler{BaseURL: baseURL, Client: httpClient}
		},
	))

	root.AddCommand(newBuiltinCmd(
		"schema <command>", "Show command parameter details",
		[]string{"revit-cli schema create_wall"},
		true,
		func(baseURL string, httpClient *http.Client) abstractions.CliCommand {
			return builtin.SchemaHandler{BaseURL: baseURL, Client: httpClient}
		},
	))

	root.AddCommand(newBuiltinCmd(
		"raw-mode [--enable | --disable]", "Query or toggle raw execution mode",
		[]string{"revit-cli raw-mode", "revit-cli raw-mode --enable", "revit-cli raw-mode --disable"},
		true,
		func(baseURL string, httpClient *http.Client) abstractions.CliCommand {
			return builtin.RawModeHandler{BaseURL: baseURL, Client: httpClient}
		},
	))

	root.AddCommand(newBuiltinCmd(
		"llms [--save <path>]", "Show Revit API reference (llms.txt)",
		[]string{"revit-cli llms", "revit-cli llms --save llms.txt"},
		true,
		func(baseURL string, httpClient *http.Client) abstractions.CliCommand {
			return builtin.LlmsHandler{BaseURL: baseURL, Client: httpClient}
		},
	))

	root.AddCommand(newBuiltinCmd(
		"list [--json]", "List running Revit instances",
		[]string{"revit-cli list", "revit-cli list --json"},
		false,
		func(_ string, _ *http.Client) abstractions.CliCommand {
			return builtin.ListHandler{}
		},
	))

	root.AddCommand(newBuiltinCmd(
		"configure <setup|teardown|check|port> [options]",
		"Manage bridge installation and configuration",
		[]string{"revit-cli configure setup", "revit-cli configure check", "revit-cli configure teardown", "revit-cli configure port"},
		false,
		func(_ string, _ *http.Client) abstractions.CliCommand {
			return builtin.ConfigureHandler{}
		},
	))

	root.AddCommand(newBuiltinCmd(
		"skill <install|show|update|uninstall> [options]",
		"Install and manage the AI agent skill (SKILL.md)",
		[]string{"revit-cli skill install --list", "revit-cli skill install --agent claude,codebuddy", "revit-cli skill update"},
		false,
		func(_ string, _ *http.Client) abstractions.CliCommand {
			return builtin.SkillHandler{}
		},
	))
}

func newBuiltinCmd(use, short string, examples []string, needsURL bool,
	factory func(baseURL string, httpClient *http.Client) abstractions.CliCommand,
) *cobra.Command {
	return &cobra.Command{
		Use:                use,
		Short:              short,
		Example:            strings.Join(examples, "\n"),
		DisableFlagParsing: true,
		SilenceUsage:       true,
		SilenceErrors:      true,
		RunE: func(cmd *cobra.Command, args []string) error {
			for _, a := range args {
				if a == "--help" || a == "-h" {
					return cmd.Help()
				}
			}

			var send abstractions.SendCommandFunc
			var baseURL string
			var httpClient *http.Client

			if needsURL {
				baseURL = resolveBaseURL()
				httpClient = &http.Client{Timeout: 0}
				send = makeSend(baseURL)
			} else {
				send = abstractions.SendCommandFunc(func(_ context.Context, _ string, _ interface{}, _ ...map[string]interface{}) int {
					fmt.Fprintln(os.Stderr, "Error: this command does not use the bridge connection.")
					return 1
				})
			}

			h := factory(baseURL, httpClient)
			exitCode = h.Handle(context.Background(), args, send)
			return nil
		},
	}
}
