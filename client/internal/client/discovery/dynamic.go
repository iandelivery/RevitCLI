package discovery

import (
	"context"
	"fmt"
	"os"
	"strconv"
	"strings"

	"revit-cli/internal/abstractions"
	"revit-cli/internal/models"
)

// DynamicCommand is a CliCommand implementation built from schema metadata.
// It replaces all hard-coded handler classes — a single type handles any
// discovered command by constructing parameters from CLI args and forwarding
// to the bridge. Mirrors C# RevitCliClient.Discovery.DynamicCommand.
type DynamicCommand struct {
	def models.CommandDef
}

// NewDynamicCommand creates a DynamicCommand from a schema CommandDef.
func NewDynamicCommand(def models.CommandDef) *DynamicCommand {
	return &DynamicCommand{def: def}
}

// Metadata returns the command metadata derived from the schema.
func (d *DynamicCommand) Metadata() abstractions.CommandMetadata {
	examples := d.def.Examples
	if examples == nil {
		examples = []string{}
	}
	return abstractions.CommandMetadata{
		Name:        d.def.Name,
		Description: d.def.Description,
		Usage:       d.buildUsage(),
		Category:    abstractions.MapCategory(d.def.Category),
		Examples:    examples,
	}
}

// Handle parses CLI args against the schema, validates required parameters,
// and forwards the command to the bridge via sendCommand.
// Mirrors C# DynamicCommand.HandleAsync.
func (d *DynamicCommand) Handle(ctx context.Context, args []string, send abstractions.SendCommandFunc) int {
	params := d.parseArgs(args)

	// Validate required parameters.
	var missing []string
	for _, p := range d.def.Parameters {
		if p.Required {
			if _, ok := params[p.Name]; !ok {
				missing = append(missing, p.Name)
			}
		}
	}
	if len(missing) > 0 {
		for _, name := range missing {
			fmt.Fprintf(os.Stderr, "Error: --%s is required\n", name)
		}
		fmt.Fprintf(os.Stderr, "Usage: %s\n", d.buildUsage())
		return 1
	}

	// Inject dry_run flag if present.
	if abstractions.HasFlag(args, "--dry-run") {
		params["dry_run"] = true
	}

	return send(ctx, d.def.Name, params)
}

// parseArgs extracts parameter values from CLI args using the schema's
// parameter definitions and short flags. Mirrors C# DynamicCommand.ParseArgs.
func (d *DynamicCommand) parseArgs(args []string) map[string]interface{} {
	result := make(map[string]interface{})

	for _, p := range d.def.Parameters {
		flags := []string{"--" + p.Name}
		if p.ShortFlag != "" {
			flags = append(flags, "-"+p.ShortFlag)
		}

		val, ok := abstractions.FindArg(args, flags...)
		if ok {
			result[p.Name] = coerce(val, p.Type)
		} else if p.Default != nil {
			result[p.Name] = p.Default
		}
	}

	return result
}

// buildUsage constructs a usage string from the schema's parameters.
// Mirrors C# DynamicCommand.BuildUsage.
func (d *DynamicCommand) buildUsage() string {
	var parts []string
	parts = append(parts, d.def.Name)

	for _, p := range d.def.Parameters {
		if p.Required {
			parts = append(parts, fmt.Sprintf("--%s <%s>", p.Name, p.Type))
		}
	}
	for _, p := range d.def.Parameters {
		if !p.Required {
			parts = append(parts, fmt.Sprintf("[--%s <%s>]", p.Name, p.Type))
		}
	}
	if d.def.SupportsDryRun {
		parts = append(parts, "[--dry-run]")
	}
	return strings.Join(parts, " ")
}

// coerce converts a string CLI value to the type declared in the schema.
// Mirrors C# DynamicCommand.Coerce.
func coerce(value, typ string) interface{} {
	switch typ {
	case "int":
		if i, err := strconv.Atoi(value); err == nil {
			return i
		}
		return value
	case "double":
		if f, err := strconv.ParseFloat(value, 64); err == nil {
			return f
		}
		return value
	case "bool":
		if b, err := strconv.ParseBool(value); err == nil {
			return b
		}
		return value
	case "int[]":
		var arr []int
		for _, part := range strings.Split(value, ",") {
			part = strings.TrimSpace(part)
			if n, err := strconv.Atoi(part); err == nil {
				arr = append(arr, n)
			} else {
				arr = append(arr, 0)
			}
		}
		return arr
	case "string[]":
		var arr []string
		for _, part := range strings.Split(value, ",") {
			arr = append(arr, strings.TrimSpace(part))
		}
		return arr
	default:
		return value
	}
}
