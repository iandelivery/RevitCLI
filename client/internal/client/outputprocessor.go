package client

import (
	"revit-cli/internal/abstractions"
)

// OutputProcessor post-processes command output with --fields, --fmt, --jq flags.
// Mirrors C# RevitCliClient.OutputProcessor.
//
// Currently these flags are forwarded to the bridge server which handles them
// natively. Client-side processing is stubbed for parity.
type OutputProcessor struct{}

// HasOutputFlags reports whether any output-processing flag is present.
// Mirrors C# OutputProcessor.HasOutputFlags.
func HasOutputFlags(args []string) bool {
	return abstractions.HasFlag(args, "--fields", "--fmt", "--jq")
}

// Process applies output post-processing to a JSON string based on CLI flags.
// Mirrors C# OutputProcessor.Process. Currently a passthrough with warnings.
func Process(output string, args []string) string {
	if abstractions.HasFlag(args, "--jq") {
		warnPrintln("[jq] Client-side jq filtering not yet implemented. Use server-side filtering instead.")
	}
	if abstractions.HasFlag(args, "--fields") {
		warnPrintln("[fields] Client-side field projection not yet implemented. Use server-side filtering instead.")
	}
	if abstractions.HasFlag(args, "--fmt") {
		if fmtVal, ok := abstractions.FindArg(args, "--fmt"); ok && fmtVal != "json" {
			warnPrintln("[fmt] Client-side format conversion to '" + fmtVal + "' not yet implemented. Use --fmt json for default output.")
		}
	}
	return output
}
