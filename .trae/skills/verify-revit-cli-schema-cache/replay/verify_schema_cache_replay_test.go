package discovery

import (
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
	"time"

	"revit-cli/internal/models"
)

const verifyOldETag = "2024.1.0:105:old"
const verifyNewETag = "2025.1.0:110:new"

func verifySchemaBody(version string) string {
	return `{"version":"` + version + `","commands":[{"name":"create_wall"}]}`
}

type verifyBridge struct {
	srv  *httptest.Server
	etag string
	body string
}

func newVerifyBridge(t *testing.T, etag, body string) *verifyBridge {
	t.Helper()
	b := &verifyBridge{etag: etag, body: body}
	b.srv = httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/api/commands" {
			http.NotFound(w, r)
			return
		}
		if r.Header.Get("If-None-Match") == `"`+b.etag+`"` {
			w.WriteHeader(http.StatusNotModified)
			return
		}
		w.Header().Set("ETag", `"`+b.etag+`"`)
		_, _ = w.Write([]byte(b.body))
	}))
	t.Cleanup(b.srv.Close)
	return b
}

func (b *verifyBridge) fetch(force bool) *models.CommandSchema {
	return NewSchemaFetcher(b.srv.URL, b.srv.Client()).Fetch(force)
}

func verifySeedDisk(t *testing.T, cache *SchemaCache, schemaBody, etag string, age time.Duration) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(cache.cachePath), 0o755); err != nil {
		t.Fatal(err)
	}
	if schemaBody != "" {
		if err := os.WriteFile(cache.cachePath, []byte(schemaBody), 0o644); err != nil {
			t.Fatal(err)
		}
		old := time.Now().Add(-age)
		if err := os.Chtimes(cache.cachePath, old, old); err != nil {
			t.Fatal(err)
		}
	}
	if etag != "" {
		if err := os.WriteFile(cache.etagPath, []byte(etag), 0o644); err != nil {
			t.Fatal(err)
		}
	}
}

func verifyBreakListSave(t *testing.T, cache *SchemaCache) {
	t.Helper()
	if err := os.Mkdir(cache.cachePath+".tmp", 0o755); err != nil {
		t.Fatal(err)
	}
}

func verifyVersion(s *models.CommandSchema) string {
	if s == nil {
		return "nothing"
	}
	return s.Version
}

func TestVerify_NotModifiedNeverServesOldList(t *testing.T) {
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())
	bridge := newVerifyBridge(t, verifyNewETag, verifySchemaBody("new"))
	cache := NewSchemaCache(bridge.srv.URL)
	verifySeedDisk(t, cache, verifySchemaBody("old"), verifyOldETag, 2*time.Hour)
	verifyBreakListSave(t, cache)

	if got := verifyVersion(bridge.fetch(false)); got != "new" {
		t.Fatalf("first fetch: served %q, want new", got)
	}

	if got := verifyVersion(bridge.fetch(false)); got != "new" {
		t.Fatalf("next run after the list save failed: served %q, want new (the bridge said not modified for a stamp whose list was never saved)", got)
	}

	if got := verifyVersion(bridge.fetch(true)); got != "new" {
		t.Fatalf("--refresh after the list save failed: served %q, want new", got)
	}
}

func TestVerify_SavedStampWithoutListStillFetches(t *testing.T) {
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())
	bridge := newVerifyBridge(t, verifyNewETag, verifySchemaBody("new"))
	verifySeedDisk(t, NewSchemaCache(bridge.srv.URL), "", verifyNewETag, 0)

	if got := verifyVersion(bridge.fetch(false)); got != "new" {
		t.Fatalf("run with a saved stamp but no saved list: served %q, want new (the bridge is up and answering)", got)
	}
}
