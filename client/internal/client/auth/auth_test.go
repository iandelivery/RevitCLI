package auth

import (
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
)

func withTempCacheDir(t *testing.T) string {
	t.Helper()
	dir := t.TempDir()
	SetCacheDir(dir)
	t.Cleanup(func() {
		SetCacheDir("")
		SetFallbackResolver(nil)
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
	path := filepath.Join(dir, "servers", "apikey_localhost_5042.txt")
	if _, err := os.Stat(path); err != nil {
		t.Fatalf("expected key file to exist: %v", err)
	}

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
		{"http://127.0.0.1:5000", "localhost_5000"},
		{"http://[::1]:5000", "__1_5000"},
	}
	for _, c := range cases {
		if got := serverKey(c.input); got != c.want {
			t.Errorf("serverKey(%q) = %q, want %q", c.input, got, c.want)
		}
	}
}

func TestGetAPIKey_FallsBackToResolverWhenNoDiskKey(t *testing.T) {
	withTempCacheDir(t)
	baseURL := "http://localhost:5012"

	calls := 0
	SetFallbackResolver(func(base string) string {
		calls++
		if base == baseURL {
			return "registry-key"
		}
		return ""
	})

	if got := GetAPIKey(baseURL); got != "registry-key" {
		t.Fatalf("expected 'registry-key' from the resolver, got %q", got)
	}
	if got := GetAPIKey(baseURL); got != "registry-key" {
		t.Fatalf("expected cached 'registry-key', got %q", got)
	}
	if calls != 1 {
		t.Fatalf("expected the resolver to run once, ran %d times", calls)
	}
}

func TestGetAPIKey_ReturnsEmptyWhenResolverYieldsNothing(t *testing.T) {
	withTempCacheDir(t)
	SetFallbackResolver(func(string) string { return "" })

	if got := GetAPIKey("http://localhost:5013"); got != "" {
		t.Fatalf("expected empty key, got %q", got)
	}
}

func TestGetAPIKey_DiskKeyWinsOverResolver(t *testing.T) {
	dir := withTempCacheDir(t)
	baseURL := "http://localhost:5014"

	SetFallbackResolver(func(string) string { return "resolver-key" })
	servers := filepath.Join(dir, "servers")
	if err := os.MkdirAll(servers, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(servers, "apikey_localhost_5014.txt"), []byte("disk-key"), 0o600); err != nil {
		t.Fatal(err)
	}

	if got := GetAPIKey(baseURL); got != "disk-key" {
		t.Fatalf("expected the disk key to win, got %q", got)
	}
}

func TestGetAPIKey_LoopbackAliasHitsSameKeyFile(t *testing.T) {
	withTempCacheDir(t)
	if err := SetAPIKey("http://localhost:5011", "key-11"); err != nil {
		t.Fatal(err)
	}

	if got := GetAPIKey("http://127.0.0.1:5011"); got != "key-11" {
		t.Fatalf("expected the loopback alias to hit the same key file, got %q", got)
	}
}

func TestGetAPIKey_DataDirScopesTheKeyStore(t *testing.T) {
	dataDir := t.TempDir()
	t.Setenv("REVIT_CLI_DATA_DIR", dataDir)
	SetCacheDir("")
	t.Cleanup(func() {
		SetCacheDir("")
		for k := range cache {
			delete(cache, k)
		}
	})

	servers := filepath.Join(dataDir, "servers")
	if err := os.MkdirAll(servers, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(servers, "apikey_localhost_5015.txt"), []byte("env-key"), 0o600); err != nil {
		t.Fatal(err)
	}

	if got := GetAPIKey("http://localhost:5015"); got != "env-key" {
		t.Fatalf("expected 'env-key' from the data-dir scoped key store, got %q", got)
	}
}
