package abstractions

import "context"

// CommandMetadata holds the descriptive metadata for a CLI command.
// Mirrors C# RevitCliClient.Abstractions.ICommandMetadata.
type CommandMetadata struct {
	Name        string
	Description string
	Usage       string
	Category    CommandCategory
	Examples    []string
}

// CliCommand is the interface implemented by all CLI commands (built-in and
// dynamic). Mirrors C# RevitCliClient.Abstractions.ICliCommand.
//
// Metadata returns the command's descriptive metadata.
// Handle executes the command with the given args and send func, returning
// the process exit code (0 for success, non-zero for failure).
type CliCommand interface {
	Metadata() CommandMetadata
	Handle(ctx context.Context, args []string, send SendCommandFunc) int
}
