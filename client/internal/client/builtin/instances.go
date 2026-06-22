package builtin

import (
	"context"
	"encoding/json"
	"fmt"

	"revit-cli/internal/abstractions"
	"revit-cli/internal/instance"
)

// ListHandler lists running Revit instances discovered from the instance registry.
type ListHandler struct{}

func (ListHandler) Metadata() abstractions.CommandMetadata {
	return abstractions.CommandMetadata{
		Name:        "list",
		Description: "List running Revit instances",
		Usage:       "list [--json]",
		Category:    abstractions.CategorySystem,
		Examples:    []string{"revit-cli.exe list", "revit-cli.exe list --json"},
	}
}

func (ListHandler) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	instances := instance.Discover()
	if len(instances) == 0 {
		fmt.Println("No running Revit instances found.")
		fmt.Println("Make sure Revit is running with the CLI Bridge add-in enabled.")
		return 0
	}

	if abstractions.HasFlag(args, "--json") {
		b, _ := json.MarshalIndent(instances, "", "  ")
		fmt.Println(string(b))
		return 0
	}

	instance.PrintInstances(instances)
	return 0
}
