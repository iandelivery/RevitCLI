package abstractions

import (
	"strconv"
	"strings"
)

// ArgHelper provides centralized, type-safe argument parsing with multi-alias
// support. Mirrors C# RevitCliClient.Abstractions.ArgHelper.
//
// All lookup functions accept variadic flag names and return the first match,
// enabling short/long flag aliases (e.g. FindArg(args, "--name", "-n")).
type ArgHelper struct{}

// FindArg returns the value following any of the given flags, or "" and false
// if none is found. Accepts both "--flag value" and inline "--flag=value"
// forms. Rejects values that look like flags (starting with "-" followed by
// a letter, e.g. "--foo" or "-n") to prevent accidental flag consumption,
// but accepts negative numbers ("-100", "-0.5").
// Mirrors C# ArgHelper.FindArg.
func FindArg(args []string, flags ...string) (string, bool) {
	for _, flag := range flags {
		for i := 0; i < len(args); i++ {
			a := args[i]
			if strings.HasPrefix(a, flag+"=") {
				return strings.TrimPrefix(a, flag+"="), true
			}
			if a == flag && i+1 < len(args) {
				val := args[i+1]
				if strings.HasPrefix(val, "-") && !isNumeric(val) {
					return "", false
				}
				return val, true
			}
		}
	}
	return "", false
}

func isNumeric(s string) bool {
	if _, err := strconv.ParseFloat(s, 64); err == nil {
		return true
	}
	_, err := strconv.ParseInt(s, 0, 64)
	return err == nil
}

// HasFlag reports whether any of the given flags is present in args.
// Mirrors C# ArgHelper.HasFlag.
func HasFlag(args []string, flags ...string) bool {
	for _, flag := range flags {
		for _, a := range args {
			if a == flag {
				return true
			}
		}
	}
	return false
}

// FirstPositional returns the first positional argument in args and the
// remaining arguments after it. It skips the global connection flags
// (--url/--pid/--revit in both "--flag value" and "--flag=value" forms) and
// any other leading flags, so built-in handlers that dispatch on a
// sub-command or command name work regardless of where the user placed the
// global flags (e.g. "revit-cli --revit 2019 schema create_wall"). Mirrors
// the flag-skipping loop in main.dispatchDynamic.
func FirstPositional(args []string) (string, []string) {
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
		return a, args[i+1:]
	}
	return "", nil
}

// GetInt returns the integer value following any of the given flags, or
// false if not found or unparseable. Mirrors C# ArgHelper.GetInt.
func GetInt(args []string, flags ...string) (int, bool) {
	val, ok := FindArg(args, flags...)
	if !ok {
		return 0, false
	}
	n, err := strconv.Atoi(val)
	if err != nil {
		return 0, false
	}
	return n, true
}

// GetDouble returns the float64 value following any of the given flags, or
// false if not found or unparseable. Mirrors C# ArgHelper.GetDouble.
func GetDouble(args []string, flags ...string) (float64, bool) {
	val, ok := FindArg(args, flags...)
	if !ok {
		return 0, false
	}
	d, err := strconv.ParseFloat(val, 64)
	if err != nil {
		return 0, false
	}
	return d, true
}

// TryParseValue auto-detects int, float64, or string from a value.
// Mirrors C# ArgHelper.TryParseValue. Returns int, then float64, then string.
func TryParseValue(value string) interface{} {
	if i, err := strconv.Atoi(value); err == nil {
		return i
	}
	if d, err := strconv.ParseFloat(value, 64); err == nil {
		return d
	}
	return value
}

// ParseIDs parses a comma-separated list of integer IDs.
// Returns nil if any segment is not a valid integer.
// Mirrors C# ArgHelper.ParseIds.
func ParseIDs(ids string) []int {
	if ids == "" {
		return nil
	}
	parts := strings.Split(ids, ",")
	result := make([]int, 0, len(parts))
	for _, p := range parts {
		trimmed := strings.TrimSpace(p)
		if trimmed == "" {
			continue
		}
		n, err := strconv.Atoi(trimmed)
		if err != nil {
			return nil
		}
		result = append(result, n)
	}
	return result
}

// ParseIDsToArray is an alias for ParseIDs returning a slice.
// Kept for API parity with C# ArgHelper.ParseIdsToArray.
func ParseIDsToArray(ids string) []int {
	return ParseIDs(ids)
}
