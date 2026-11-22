package main

import (
	"net/http"
	"net/http/httptest"
	"os"
	"reflect"
	"strings"
	"testing"

	"revit-cli/internal/client/auth"
	"revit-cli/internal/client/discovery"
)

func newRejectedFetcher(t *testing.T) (*discovery.SchemaFetcher, string, func()) {
	t.Helper()
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusUnauthorized)
		_, _ = w.Write([]byte(`{"error":"Unauthorized. Provide 'Authorization: Bearer <api_key>' header.","auth_enabled":true}`))
	}))
	f := discovery.NewSchemaFetcher(srv.URL, srv.Client())
	if f.FetchCommand("create_wall") != nil {
		t.Fatal("expected no command definition from a 401 server")
	}
	return f, srv.URL, srv.Close
}

func TestRejectAuthError_NoKeyHintsSetupAndUpdate(t *testing.T) {
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())
	auth.SetCacheDir("")
	t.Cleanup(func() { auth.SetCacheDir("") })

	f, url, closeSrv := newRejectedFetcher(t)
	defer closeSrv()

	var code int
	var handled bool
	stderr := captureStderrMain(t, func() {
		code, handled = rejectAuthError(url, f)
	})

	if !handled {
		t.Fatal("expected rejectAuthError to handle the rejection")
	}
	if code != 2 {
		t.Fatalf("expected exit code 2, got %d", code)
	}
	if !strings.Contains(stderr, "no API key is available") {
		t.Fatalf("expected the missing-key hint, got %q", stderr)
	}
	if !strings.Contains(stderr, "restart Revit") {
		t.Fatalf("expected the restart remedy, got %q", stderr)
	}
}

func TestRejectAuthError_RejectedKeyHintsResync(t *testing.T) {
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())
	auth.SetCacheDir("")
	t.Cleanup(func() { auth.SetCacheDir("") })

	f, url, closeSrv := newRejectedFetcher(t)
	defer closeSrv()
	if err := auth.SetAPIKey(url, "wrong-key"); err != nil {
		t.Fatal(err)
	}

	var code int
	var handled bool
	stderr := captureStderrMain(t, func() {
		code, handled = rejectAuthError(url, f)
	})

	if !handled {
		t.Fatal("expected rejectAuthError to handle the rejection")
	}
	if code != 2 {
		t.Fatalf("expected exit code 2, got %d", code)
	}
	if !strings.Contains(stderr, "rejected the API key") {
		t.Fatalf("expected the rejected-key hint, got %q", stderr)
	}
	if !strings.Contains(stderr, "re-sync") {
		t.Fatalf("expected the re-sync remedy, got %q", stderr)
	}
}

func TestRejectAuthError_NoRejectionMeansNotHandled(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNotFound)
	}))
	defer srv.Close()
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())
	auth.SetCacheDir("")
	t.Cleanup(func() { auth.SetCacheDir("") })

	f := discovery.NewSchemaFetcher(srv.URL, srv.Client())
	f.FetchCommand("missing_command")

	var handled bool
	stderr := captureStderrMain(t, func() {
		_, handled = rejectAuthError(srv.URL, f)
	})

	if handled {
		t.Fatal("expected rejectAuthError not to handle a 404")
	}
	if stderr != "" {
		t.Fatalf("expected no output for a non-401, got %q", stderr)
	}
}

func captureStderrMain(t *testing.T, fn func()) string {
	t.Helper()
	r, w, err := os.Pipe()
	if err != nil {
		t.Fatal(err)
	}
	orig := os.Stderr
	os.Stderr = w
	fn()
	w.Close()
	os.Stderr = orig
	data := make([]byte, 1024)
	n, _ := r.Read(data)
	return string(data[:n])
}

func TestParseUnitsSpecE(t *testing.T) {
	tests := []struct {
		name    string
		specs   []string
		want    interface{}
		wantErr bool
	}{
		{name: "empty", specs: nil, want: nil},
		{name: "project", specs: []string{"project"}, want: "project"},
		{
			name:  "pairs",
			specs: []string{"length=ft", "angle=rad"},
			want:  map[string]interface{}{"length": "ft", "angle": "rad"},
		},
		{name: "garbage", specs: []string{"feet"}, wantErr: true},
		{name: "missing unit", specs: []string{"length="}, wantErr: true},
		{name: "missing kind", specs: []string{"=ft"}, wantErr: true},
		{name: "mixed after project", specs: []string{"project", "length=ft"}, wantErr: true},
		{name: "mixed before project", specs: []string{"length=ft", "project"}, wantErr: true},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got, err := parseUnitsSpecE(tt.specs)
			if tt.wantErr {
				if err == nil {
					t.Fatalf("expected error, got %v", got)
				}
				return
			}
			if err != nil {
				t.Fatalf("unexpected error: %v", err)
			}
			if !reflect.DeepEqual(got, tt.want) {
				t.Fatalf("got %v, want %v", got, tt.want)
			}
		})
	}
}

func TestParseGlobalOptions_UnitsRepeatable(t *testing.T) {
	opts := parseGlobalOptions([]string{"--units", "length=ft", "--pid", "42", "--units", "angle=rad"})
	if opts.pid != 42 {
		t.Fatalf("pid = %d, want 42", opts.pid)
	}
	if len(opts.units) != 2 || opts.units[0] != "length=ft" || opts.units[1] != "angle=rad" {
		t.Fatalf("units = %v", opts.units)
	}
	inline := parseGlobalOptions([]string{"--units=length=ft", "get_units"})
	if len(inline.units) != 1 || inline.units[0] != "length=ft" {
		t.Fatalf("inline units = %v", inline.units)
	}
}
