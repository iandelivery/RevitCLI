package builtin

import (
	"context"
	"fmt"
	"os"

	"revit-cli/internal/abstractions"
)

// ExecuteRawHandler executes C# or Python code on the bridge via the
// "execute_raw" command. This is a stub that forwards to the bridge.
// Mirrors C# RevitCliClient.BuiltIn.ExecuteRawCommand.
type ExecuteRawHandler struct{}

func (ExecuteRawHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "execute_raw",
		Description: "Execute C# or Python code on the bridge",
		Usage:       "execute_raw --code <code> [--lang csharp|python]",
		Category:    abstractions.CategoryRaw,
		Examples:    []string{`revit-cli.exe execute_raw --lang csharp --code "return doc.Title;"`},
	}
}

func (ExecuteRawHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	code, ok := abstractions.FindArg(args, "--code")
	if !ok || code == "" {
		fmt.Fprintln(os.Stderr, "Error: --code is required")
		return 1
	}

	lang := "csharp"
	if l, ok := abstractions.FindArg(args, "--lang"); ok && l != "" {
		lang = l
	}

	params := map[string]interface{}{
		"code": code,
		"lang": lang,
	}
	return send(ctx, "execute_raw", params)
}
