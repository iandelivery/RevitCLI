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

const testOldETag = "2024.1.0:105:old"
const testNewETag = "2025.1.0:110:new"

func testSchemaBody(version string) string {
	return `{"version":"` + version + `","commands":[{"name":"create_wall"}]}`
}

// newETagBridge serves body with etag and answers 304 when If-None-Match
// carries that etag, like the bridge's /api/commands endpoint.
func newETagBridge(t *testing.T, etag, body string) (*httptest.Server, *SchemaFetcher) {
	t.Helper()
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/api/commands" {
			http.NotFound(w, r)
			return
		}
		if r.Header.Get("If-None-Match") == `"`+etag+`"` {
			w.WriteHeader(http.StatusNotModified)
			return
		}
		w.Header().Set("ETag", `"`+etag+`"`)
		_, _ = w.Write([]byte(body))
	}))
	t.Cleanup(srv.Close)
	return srv, NewSchemaFetcher(srv.URL, srv.Client())
}

// seedDisk writes a cached list (aged) and etag through the fetcher's own
// per-server paths.
func seedDisk(t *testing.T, c *SchemaCache, schemaBody, etag string, age time.Duration) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(c.cachePath), 0o755); err != nil {
		t.Fatal(err)
	}
	if schemaBody != "" {
		if err := os.WriteFile(c.cachePath, []byte(schemaBody), 0o644); err != nil {
			t.Fatal(err)
		}
		old := time.Now().Add(-age)
		if err := os.Chtimes(c.cachePath, old, old); err != nil {
			t.Fatal(err)
		}
	}
	if etag != "" {
		if err := os.WriteFile(c.etagPath, []byte(etag), 0o644); err != nil {
			t.Fatal(err)
		}
	}
}

func servedVersion(s *models.CommandSchema) string {
	if s == nil {
		return "nothing"
	}
	return s.Version
}

func TestFetch_ListSaveFailureKeepsOldPair(t *testing.T) {
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())
	srv, f := newETagBridge(t, testNewETag, testSchemaBody("new"))
	cache := NewSchemaCache(srv.URL)
	seedDisk(t, cache, testSchemaBody("old"), testOldETag, 2*time.Hour)
	// A directory at the tmp path makes Save fail the way a full disk or a
	// locked file would.
	if err := os.Mkdir(cache.cachePath+".tmp", 0o755); err != nil {
		t.Fatal(err)
	}

	if got := servedVersion(f.Fetch(false)); got != "new" {
		t.Fatalf("first fetch served %q, want new", got)
	}
	if got := servedVersion(f.Fetch(false)); got != "new" {
		t.Fatalf("run after the list save failed served %q, want new: the etag must not be saved when the list is not", got)
	}
	if got := servedVersion(f.Fetch(true)); got != "new" {
		t.Fatalf("--refresh after the list save failed served %q, want new", got)
	}
}

func TestFetch_StampWithoutListFetchesFresh(t *testing.T) {
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())
	srv, f := newETagBridge(t, testNewETag, testSchemaBody("new"))
	seedDisk(t, NewSchemaCache(srv.URL), "", testNewETag, 0)

	if got := servedVersion(f.Fetch(false)); got != "new" {
		t.Fatalf("fetch with a stamp but no list served %q, want new: the bridge is up and answering", got)
	}
}

func newAuthTestFetcher(t *testing.T, status int) (*SchemaFetcher, func()) {
	t.Helper()
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(status)
		_, _ = w.Write([]byte(`{"error":"Unauthorized. Provide 'Authorization: Bearer <api_key>' header.","auth_enabled":true}`))
	}))
	f := NewSchemaFetcher(srv.URL, srv.Client())
	return f, srv.Close
}

func TestFetchCommand_UnauthorizedSetsAuthRejected(t *testing.T) {
	f, closeSrv := newAuthTestFetcher(t, http.StatusUnauthorized)
	defer closeSrv()

	if def := f.FetchCommand("create_wall"); def != nil {
		t.Fatalf("expected no command definition on 401, got %v", def)
	}
	if !f.AuthRejected() {
		t.Fatal("expected AuthRejected to be true after a 401")
	}
}

func TestFetch_UnauthorizedSetsAuthRejectedAndReturnsNoSchema(t *testing.T) {
	f, closeSrv := newAuthTestFetcher(t, http.StatusUnauthorized)
	defer closeSrv()

	if schema := f.Fetch(false); schema != nil {
		t.Fatalf("expected no schema on 401 without a stale cache, got %v", schema)
	}
	if !f.AuthRejected() {
		t.Fatal("expected AuthRejected to be true after a 401")
	}
}

func TestFetchCommand_ServerErrorDoesNotSetAuthRejected(t *testing.T) {
	f, closeSrv := newAuthTestFetcher(t, http.StatusInternalServerError)
	defer closeSrv()

	if def := f.FetchCommand("create_wall"); def != nil {
		t.Fatalf("expected no command definition on 500, got %v", def)
	}
	if f.AuthRejected() {
		t.Fatal("expected AuthRejected to stay false on a 500")
	}
}

func TestFetchCommand_NotFoundDoesNotSetAuthRejected(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusNotFound)
	}))
	defer srv.Close()
	t.Setenv("REVIT_CLI_DATA_DIR", t.TempDir())

	f := NewSchemaFetcher(srv.URL, srv.Client())
	if def := f.FetchCommand("missing_command"); def != nil {
		t.Fatalf("expected no command definition on 404, got %v", def)
	}
	if f.AuthRejected() {
		t.Fatal("expected AuthRejected to stay false on a 404")
	}
}
