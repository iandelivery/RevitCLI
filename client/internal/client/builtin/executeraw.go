package builtin

import (
	"context"
	"fmt"
	"io"
	"os"

	"revit-cli/internal/abstractions"
)

// ExecuteRawHandler executes C# code on the bridge via the
// "execute_raw" command. This is a stub that forwards to the bridge.
// Mirrors C# RevitCliClient.BuiltIn.ExecuteRawCommand.
type ExecuteRawHandler struct{}

func (ExecuteRawHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "execute_raw",
		Description: "Execute C# code on the bridge",
		Usage:       "execute_raw --code <code> | --file <path> | --stdin [--language csharp]",
		Category:    abstractions.CategoryRaw,
		Examples: []string{
			`revit-cli.exe execute_raw --language csharp --code "return doc.Title;"`,
			`revit-cli.exe execute_raw --file script.cs`,
			`Get-Content script.cs -Raw | revit-cli.exe execute_raw --stdin`,
		},
	}
}

func (ExecuteRawHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	code, hasCode := abstractions.FindArg(args, "--code", "-c")
	filePath, hasFile := abstractions.FindArg(args, "--file")
	hasStdin := abstractions.HasFlag(args, "--stdin")

	sources := 0
	if hasCode {
		sources++
	}
	if hasFile {
		sources++
	}
	if hasStdin {
		sources++
	}
	if sources > 1 {
		fmt.Fprintln(os.Stderr, "Error: pass only one of --code, --file, or --stdin")
		return 1
	}

	if hasFile && filePath != "" {
		data, err := os.ReadFile(filePath)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Error reading file %s: %v\n", filePath, err)
			return 1
		}
		code = string(data)
		hasCode = true
	}

	if hasStdin {
		if stdinIsTTY() {
			fmt.Fprintln(os.Stderr, "Error: --stdin needs piped input, not an interactive terminal")
			return 1
		}
		data, err := io.ReadAll(os.Stdin)
		if err != nil {
			fmt.Fprintf(os.Stderr, "Error reading stdin: %v\n", err)
			return 1
		}
		code = string(data)
		hasCode = true
	}

	if !hasCode || code == "" {
		fmt.Fprintln(os.Stderr, "Error: --code, --file, or --stdin is required")
		return 1
	}

	lang := "csharp"
	if l, ok := abstractions.FindArg(args, "--language", "-l", "--lang"); ok && l != "" {
		lang = l
	}

	params := map[string]interface{}{
		"code":     code,
		"language": lang,
	}
	return send(ctx, "execute_raw", params)
}
