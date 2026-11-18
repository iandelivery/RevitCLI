package client

import (
	"net/http"
	"net/http/httptest"
	"testing"
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

func TestHandleLegacyResponse_UnauthorizedWithoutStatusExitsConnect(t *testing.T) {
	resp := newLegacyResponse(t, http.StatusUnauthorized,
		`{"error":"Unauthorized. Provide 'Authorization: Bearer <api_key>' header.","auth_enabled":true}`)

	c := NewSseClient("http://localhost:5012")
	code, err := c.handleLegacyResponse(resp)

	if err != nil {
		t.Fatalf("expected no error, got %v", err)
	}
	if code != ExitConnect {
		t.Fatalf("expected exit code %d for a 401, got %d", ExitConnect, code)
	}
}

func TestHandleLegacyResponse_UnauthorizedWithStatusSkipsAuthHint(t *testing.T) {
	resp := newLegacyResponse(t, http.StatusUnauthorized,
		`{"status":"error","message":"Element with ID 999 not found.","task_id":"t-1"}`)

	c := NewSseClient("http://localhost:5012")
	code, err := c.handleLegacyResponse(resp)

	if err != nil {
		t.Fatalf("expected no error, got %v", err)
	}
	if code != ExitConnect {
		t.Fatalf("expected exit code %d for any 401, got %d", ExitConnect, code)
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
