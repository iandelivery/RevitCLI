package client

import (
	"fmt"
	"strings"

	"revit-cli/internal/abstractions"
)

// argShortcuts mirrors C# HelpText.ArgShortcuts.
var argShortcuts = []struct {
	Short, Long, Description string
}{
	{"-i", "--id", "Element ID"},
	{"-e", "--element-id", "Element ID"},
	{"-w", "--wall-id", "Wall ID"},
	{"-l", "--level-id", "Level ID"},
	{"-n", "--name", "Name"},
	{"-v", "--value", "Value"},
	{"-c", "--category", "Category"},
	{"-f", "--family / --format", "Family name / Export format"},
	{"-s", "--symbol / --selected", "Symbol name / Selected mode"},
	{"-si", "--symbol-id", "Symbol element ID"},
	{"-a", "--angle / --all-*", "Angle / All"},
	{"-o", "--output / --output-dir", "Output path"},
	{"-t", "--type", "Type"},
	{"-j", "--json", "JSON data"},
	{"-fl", "--file", "File path"},
	{"-vi", "--view-id", "View ID"},
	{"-vn", "--view-name", "View name"},
	{"-ti", "--template-id / --task-id", "Template ID / Task ID"},
	{"-st", "--steps", "Steps"},
}

// Generate builds the help text for the CLI client.
// Mirrors C# RevitCliClient.HelpText.Generate.
func Generate(registry *CommandRegistry) string {
	var sb strings.Builder

	sb.WriteString("Revit CLI Client (Go) - Command-line tool for AI agents to drive Autodesk Revit\n\n")
	sb.WriteString("Usage:\n")
	sb.WriteString("  revit-cli.exe [--url <url>] <command> [arguments]\n\n")

	sb.WriteString("Argument Shortcuts:\n")
	for _, sc := range argShortcuts {
		sb.WriteString(fmt.Sprintf("  %-4s %-26s %s\n", sc.Short, sc.Long, sc.Description))
	}
	sb.WriteString("\n")

	sb.WriteString("Commands:\n\n")

	allCommands := registry.GetAllCommands()
	seenCategories := make(map[abstractions.CommandCategory]bool)

	// Print commands grouped by the standard category order.
	for _, category := range abstractions.CategoryOrder {
		var commands []abstractions.CliCommand
		for _, cmd := range allCommands {
			if cmd.Metadata().Category == category {
				commands = append(commands, cmd)
			}
		}
		if len(commands) == 0 {
			continue
		}
		sb.WriteString(fmt.Sprintf("  [%s]\n", category.DisplayName()))
		for _, cmd := range commands {
			m := cmd.Metadata()
			sb.WriteString(fmt.Sprintf("  %-24s %s\n", m.Name, m.Description))
		}
		sb.WriteString("\n")
		seenCategories[category] = true
	}

	// Print any remaining custom categories.
	seenCmds := make(map[string]bool)
	for _, cmd := range allCommands {
		cat := cmd.Metadata().Category
		if seenCategories[cat] {
			continue
		}
		if seenCmds[cat.DisplayName()] {
			continue
		}
		seenCmds[cat.DisplayName()] = true
		var commands []abstractions.CliCommand
		for _, c := range allCommands {
			if c.Metadata().Category == cat {
				commands = append(commands, c)
			}
		}
		sb.WriteString(fmt.Sprintf("  [%s]\n", cat.DisplayName()))
		for _, c := range commands {
			m := c.Metadata()
			sb.WriteString(fmt.Sprintf("  %-24s %s\n", m.Name, m.Description))
		}
		sb.WriteString("\n")
	}

	sb.WriteString("  [HTTP]\n")
	sb.WriteString("  status                  Server status (HTTP GET)\n")
	sb.WriteString("  health                  Health check (HTTP GET)\n\n")

	sb.WriteString("Usage Details:\n")
	for _, cmd := range allCommands {
		m := cmd.Metadata()
		sb.WriteString(fmt.Sprintf("  %s\n    %s\n", m.Name, m.Usage))
	}
	sb.WriteString("\n")

	sb.WriteString("Examples:\n")
	for _, cmd := range allCommands {
		m := cmd.Metadata()
		if len(m.Examples) == 0 {
			continue
		}
		for _, ex := range m.Examples {
			sb.WriteString(fmt.Sprintf("  %s\n", ex))
		}
	}
	sb.WriteString(`  revit-cli.exe raw -j "{\"command\":\"ping\"}"` + "\n\n")

	sb.WriteString("Options:\n")
	sb.WriteString("  --url <url>             Set Revit CLI server address (default: auto-discover)\n")
	sb.WriteString("  --pid <pid>             Connect to a specific Revit instance by process ID\n")
	sb.WriteString("  --revit <version>       Connect to a specific Revit version (e.g. 2022)\n")
	sb.WriteString("  --help, -h              Show help\n")
	sb.WriteString("  --version, -V           Show version\n")

	return sb.String()
}
