package builtin

import (
	"context"
	"testing"

	"revit-cli/internal/abstractions"
)

type capturedExecuteRaw struct {
	command    string
	parameters interface{}
}

// stubSend captures the command and parameters a builtin handler forwards to
// the bridge and returns a fixed exit code.
func stubSend(t *testing.T) (abstractions.SendCommandFunc, *capturedExecuteRaw) {
	t.Helper()
	captured := &capturedExecuteRaw{}
	return func(_ context.Context, command string, parameters interface{}, _ ...map[string]interface{}) int {
		captured.command = command
		captured.parameters = parameters
		return 0
	}, captured
}

func TestExecuteRawShortFlagsAndLanguageParam(t *testing.T) {
	tests := []struct {
		name         string
		args         []string
		wantCode     string
		wantLanguage string
	}{
		{
			name:         "long flags",
			args:         []string{"--code", "return doc.Title;", "--language", "python"},
			wantCode:     "return doc.Title;",
			wantLanguage: "python",
		},
		{
			name:         "short flags",
			args:         []string{"-c", "return 1;", "-l", "csharp"},
			wantCode:     "return 1;",
			wantLanguage: "csharp",
		},
		{
			name:         "inline forms",
			args:         []string{"--code=return 2;", "--language=python"},
			wantCode:     "return 2;",
			wantLanguage: "python",
		},
		{
			name:         "legacy --lang alias",
			args:         []string{"--lang", "python", "--code", "return 3;"},
			wantCode:     "return 3;",
			wantLanguage: "python",
		},
		{
			name:         "language defaults to csharp",
			args:         []string{"--code", "return 4;"},
			wantCode:     "return 4;",
			wantLanguage: "csharp",
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			send, captured := stubSend(t)
			code := ExecuteRawHandler{}.Handle(context.Background(), tt.args, send)
			if code != 0 {
				t.Fatalf("Handle exit = %d, want 0", code)
			}
			if captured.command != "execute_raw" {
				t.Fatalf("command = %q, want execute_raw", captured.command)
			}
			params, ok := captured.parameters.(map[string]interface{})
			if !ok {
				t.Fatalf("parameters type = %T, want map[string]interface{}", captured.parameters)
			}
			if params["code"] != tt.wantCode {
				t.Fatalf("code = %v, want %q", params["code"], tt.wantCode)
			}
			// The bridge schema names the parameter "language"; the legacy
			// "lang" key was silently ignored by the bridge before the fix.
			if params["language"] != tt.wantLanguage {
				t.Fatalf("language = %v, want %q", params["language"], tt.wantLanguage)
			}
			if _, exists := params["lang"]; exists {
				t.Fatalf(`parameters must not contain legacy "lang" key`)
			}
		})
	}
}

func TestExecuteRawRequiresCode(t *testing.T) {
	send := func(_ context.Context, _ string, _ interface{}, _ ...map[string]interface{}) int {
		t.Fatal("send must not be called when code is missing")
		return 0
	}
	for _, args := range [][]string{nil, {}, {"-c"}, {"--code", ""}} {
		if code := (ExecuteRawHandler{}).Handle(context.Background(), args, send); code != 1 {
			t.Fatalf("Handle(%v) exit = %d, want 1", args, code)
		}
	}
}
