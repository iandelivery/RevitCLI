package auth

import (
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
)

// withTempCacheDir swaps the package cache dir to a temp dir for the
// duration of a test, ensuring GetAPIKey/SetAPIKey don't pollute the
// real user cache.
func withTempCacheDir(t *testing.T) string {
	t.Helper()
	dir := t.TempDir()
	SetCacheDir(dir)
	t.Cleanup(func() {
		// Reset to default by clearing cache entries.
		for k := range cache {
			delete(cache, k)
		}
	})
	return dir
}

func TestSetAndGetAPIKey_RoundTrips(t *testing.T) {
	withTempCacheDir(t)
	baseURL := "http://localhost:5041"

	if err := SetAPIKey(baseURL, "secret-token"); err != nil {
		t.Fatalf("SetAPIKey failed: %v", err)
	}

	got := GetAPIKey(baseURL)
	if got != "secret-token" {
		t.Fatalf("expected 'secret-token', got %q", got)
	}
}

func TestGetAPIKey_ReturnsEmptyWhenUnset(t *testing.T) {
	withTempCacheDir(t)
	got := GetAPIKey("http://localhost:9999")
	if got != "" {
		t.Fatalf("expected empty key, got %q", got)
	}
}

func TestSetAPIKey_EmptyKeyClearsAndDeletesFile(t *testing.T) {
	dir := withTempCacheDir(t)
	baseURL := "http://localhost:5042"

	if err := SetAPIKey(baseURL, "key-to-clear"); err != nil {
		t.Fatalf("SetAPIKey failed: %v", err)
	}
	// Verify file exists.
	path := filepath.Join(dir, "servers", "apikey_localhost_5042.txt")
	if _, err := os.Stat(path); err != nil {
		t.Fatalf("expected key file to exist: %v", err)
	}

	// Now clear it.
	if err := SetAPIKey(baseURL, ""); err != nil {
		t.Fatalf("SetAPIKey clear failed: %v", err)
	}
	if got := GetAPIKey(baseURL); got != "" {
		t.Fatalf("expected empty key after clear, got %q", got)
	}
	if _, err := os.Stat(path); !os.IsNotExist(err) {
		t.Fatalf("expected key file to be deleted, got err=%v", err)
	}
}

func TestGetAPIKey_PerServerIsolation(t *testing.T) {
	withTempCacheDir(t)
	if err := SetAPIKey("http://localhost:5041", "key-41"); err != nil {
		t.Fatal(err)
	}
	if err := SetAPIKey("http://localhost:5042", "key-42"); err != nil {
		t.Fatal(err)
	}
	if got := GetAPIKey("http://localhost:5041"); got != "key-41" {
		t.Fatalf("server 5041: expected 'key-41', got %q", got)
	}
	if got := GetAPIKey("http://localhost:5042"); got != "key-42" {
		t.Fatalf("server 5042: expected 'key-42', got %q", got)
	}
}

func TestWithAuth_AttachesBearerHeaderWhenKeyPresent(t *testing.T) {
	withTempCacheDir(t)
	baseURL := "http://localhost:5041"
	if err := SetAPIKey(baseURL, "my-secret"); err != nil {
		t.Fatal(err)
	}

	req := httptest.NewRequest(http.MethodGet, baseURL+"/api/commands", nil)
	WithAuth(req, baseURL)

	got := req.Header.Get("Authorization")
	if got != "Bearer my-secret" {
		t.Fatalf("expected 'Bearer my-secret', got %q", got)
	}
}

func TestWithAuth_LeavesRequestUntouchedWhenNoKey(t *testing.T) {
	withTempCacheDir(t)
	baseURL := "http://localhost:9999"

	req := httptest.NewRequest(http.MethodGet, baseURL+"/api/commands", nil)
	WithAuth(req, baseURL)

	if got := req.Header.Get("Authorization"); got != "" {
		t.Fatalf("expected empty Authorization header, got %q", got)
	}
}

func TestWithAuth_HandlesNilRequest(t *testing.T) {
	// Should not panic on nil request.
	if r := WithAuth(nil, "http://localhost:5041"); r != nil {
		t.Fatalf("expected nil, got %v", r)
	}
}

func TestServerKey_DerivesSafeFilename(t *testing.T) {
	cases := []struct {
		input string
		want  string
	}{
		{"", "default"},
		{"http://localhost:5041", "localhost_5041"},
		{"https://localhost:5041", "localhost_5041"},
		{"http://127.0.0.1:5000", "127_0_0_1_5000"},
		{"http://[::1]:5000", "__1_5000"},
	}
	for _, c := range cases {
		if got := serverKey(c.input); got != c.want {
			t.Errorf("serverKey(%q) = %q, want %q", c.input, got, c.want)
		}
	}
}
