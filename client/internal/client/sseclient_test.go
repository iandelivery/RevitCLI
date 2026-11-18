package client

import (
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"strings"
	"testing"

	"revit-cli/internal/client/auth"
)

func newLegacyResponse(t *testing.T, status int, body string) *http.Response {
	t.Helper()
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(status)
		_, _ = w.Write([]byte(body))
	}))
	t.Cleanup(srv.Close)

	resp, err := srv.Client().Get(srv.URL)
	if err != nil {
		t.Fatalf("request failed: %v", err)
	}
	t.Cleanup(func() { resp.Body.Close() })
	return resp
}

func captureStderr(t *testing.T, fn func()) string {
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
	data, _ := io.ReadAll(r)
	return string(data)
}

func TestHandleLegacyResponse_UnauthorizedWithoutStatusExitsConnect(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusUnauthorized)
		_, _ = w.Write([]byte(`{"error":"Unauthorized. Provide 'Authorization: Bearer <api_key>' header.","auth_enabled":true}`))
	}))
	defer srv.Close()
	auth.SetCacheDir(t.TempDir())
	defer auth.SetCacheDir("")

	resp := newLegacyResponse(t, http.StatusUnauthorized,
		`{"error":"Unauthorized. Provide 'Authorization: Bearer <api_key>' header.","auth_enabled":true}`)

	c := NewSseClient(srv.URL)
	var code int
	stderr := captureStderr(t, func() {
		code, _ = c.handleLegacyResponse(resp)
	})

	if code != ExitConnect {
		t.Fatalf("expected exit code %d for a 401, got %d", ExitConnect, code)
	}
	if !strings.Contains(stderr, "No API key is available") {
		t.Fatalf("expected the missing-key hint on stderr, got %q", stderr)
	}
}

func TestHandleLegacyResponse_RejectedKeyHintsResync(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusUnauthorized)
		_, _ = w.Write([]byte(`{"error":"Unauthorized. Provide 'Authorization: Bearer <api_key>' header.","auth_enabled":true}`))
	}))
	defer srv.Close()
	auth.SetCacheDir(t.TempDir())
	defer auth.SetCacheDir("")
	if err := auth.SetAPIKey(srv.URL, "wrong-key"); err != nil {
		t.Fatal(err)
	}

	resp := newLegacyResponse(t, http.StatusUnauthorized,
		`{"error":"Unauthorized. Provide 'Authorization: Bearer <api_key>' header.","auth_enabled":true}`)

	c := NewSseClient(srv.URL)
	var code int
	stderr := captureStderr(t, func() {
		code, _ = c.handleLegacyResponse(resp)
	})

	if code != ExitConnect {
		t.Fatalf("expected exit code %d for a 401, got %d", ExitConnect, code)
	}
	if !strings.Contains(stderr, "The API key was rejected") {
		t.Fatalf("expected the rejected-key hint on stderr, got %q", stderr)
	}
}

func TestHandleLegacyResponse_UnauthorizedWithStatusSkipsAuthHint(t *testing.T) {
	resp := newLegacyResponse(t, http.StatusUnauthorized,
		`{"status":"error","message":"Element with ID 999 not found.","task_id":"t-1"}`)

	c := NewSseClient("http://localhost:5012")
	var code int
	stderr := captureStderr(t, func() {
		code, _ = c.handleLegacyResponse(resp)
	})

	if code != ExitConnect {
		t.Fatalf("expected exit code %d for any 401, got %d", ExitConnect, code)
	}
	if strings.Contains(stderr, "API key") {
		t.Fatalf("expected no auth hint when the body carries a command status, got %q", stderr)
	}
}

func TestHandleLegacyResponse_ServerErrorStillExitsCommandFail(t *testing.T) {
	resp := newLegacyResponse(t, http.StatusInternalServerError, `{"message":"boom"}`)

	c := NewSseClient("http://localhost:5012")
	code, err := c.handleLegacyResponse(resp)

	if err != nil {
		t.Fatalf("expected no error, got %v", err)
	}
	if code != ExitCommandFail {
		t.Fatalf("expected exit code %d for a 500, got %d", ExitCommandFail, code)
	}
}
