// Package client contains the core CLI client implementation: command
// registry, SSE transport, schema discovery, built-in commands, and help text.
// Mirrors C# RevitCliClient project.
package client

import (
	"log"
	"strings"

	"revit-cli/internal/abstractions"
)

// CommandRegistry holds registered CLI commands with case-insensitive name
// lookup while preserving registration order.
// Mirrors C# RevitCliClient.CommandRegistry.
type CommandRegistry struct {
	commands map[string]abstractions.CliCommand
	order    []abstractions.CliCommand
}

// NewCommandRegistry creates an empty registry.
func NewCommandRegistry() *CommandRegistry {
	return &CommandRegistry{
		commands: make(map[string]abstractions.CliCommand),
	}
}

// Register adds a command under its primary name. If a command with the same
// (case-insensitive) name already exists, the new command is NOT registered
// and a warning is logged. This prevents dynamic commands from silently
// overwriting built-in commands.
func (r *CommandRegistry) Register(cmd abstractions.CliCommand) {
	if cmd == nil {
		return
	}
	name := toLower(cmd.Metadata().Name)
	if _, exists := r.commands[name]; exists {
		log.Printf("[registry] command %q already registered, skipping duplicate from %T", cmd.Metadata().Name, cmd)
		return
	}
	r.order = append(r.order, cmd)
	r.commands[name] = cmd
}

// RegisterAlias maps an alias to an already-registered command.
// Mirrors C# CommandRegistry.RegisterAlias.
func (r *CommandRegistry) RegisterAlias(alias, targetCommandName string) {
	target, ok := r.commands[toLower(targetCommandName)]
	if ok {
		r.commands[toLower(alias)] = target
	}
}

// TryGetCommand looks up a command by name (case-insensitive).
// Returns nil, false if not found.
func (r *CommandRegistry) TryGetCommand(name string) (abstractions.CliCommand, bool) {
	cmd, ok := r.commands[toLower(name)]
	if !ok {
		return nil, false
	}
	return cmd, true
}

// GetAllCommands returns all registered commands in registration order.
func (r *CommandRegistry) GetAllCommands() []abstractions.CliCommand {
	out := make([]abstractions.CliCommand, len(r.order))
	copy(out, r.order)
	return out
}

// Count returns the number of primary registered commands.
func (r *CommandRegistry) Count() int {
	return len(r.order)
}

func toLower(s string) string {
	return strings.ToLower(s)
}
