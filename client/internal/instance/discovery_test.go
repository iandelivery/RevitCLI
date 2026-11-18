package instance

import (
	"os"
	"path/filepath"
	"testing"
)

func TestParseVersion_AcceptsValidYears(t *testing.T) {
	cases := []int{2019, 2020, 2021, 2022, 2023, 2024, 2099}
	for _, year := range cases {
		v, ok := ParseVersion(itoa(year))
		if !ok {
			t.Errorf("expected %d to parse successfully", year)
		}
		if v != year {
			t.Errorf("expected %d, got %d", year, v)
		}
	}
}

func TestParseVersion_RejectsOutOfRange(t *testing.T) {
	cases := []int{2018, 2100, 0, 1999}
	for _, year := range cases {
		if _, ok := ParseVersion(itoa(year)); ok {
			t.Errorf("expected %d to be rejected as out of range", year)
		}
	}
}

func TestParseVersion_RejectsNonNumeric(t *testing.T) {
	cases := []string{"abc", "", "20a2", "v2022"}
	for _, s := range cases {
		if _, ok := ParseVersion(s); ok {
			t.Errorf("expected %q to be rejected", s)
		}
	}
}

func TestMatchInstanceFile_AcceptsRevitPrefix(t *testing.T) {
	cases := []string{
		"revit-2022-12345.json",
		"revit-2019-1.json",
		"revit-x.json",
	}
	for _, name := range cases {
		if !matchInstanceFile(name) {
			t.Errorf("expected %q to match", name)
		}
	}
}

func TestMatchInstanceFile_RejectsNonMatching(t *testing.T) {
	cases := []string{
		"foo.json",
		"revit-123.txt",
		"revit-",
		"",
		"revit.json",
	}
	for _, name := range cases {
		if matchInstanceFile(name) {
			t.Errorf("expected %q to be rejected", name)
		}
	}
}

func withTempInstancesDir(t *testing.T) string {
	t.Helper()
	dataDir := t.TempDir()
	t.Setenv("REVIT_CLI_DATA_DIR", dataDir)
	if err := os.MkdirAll(filepath.Join(dataDir, "instances"), 0o755); err != nil {
		t.Fatal(err)
	}
	return dataDir
}

func writeInstanceFile(t *testing.T, dataDir, name, content string) {
	t.Helper()
	path := filepath.Join(dataDir, "instances", name)
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}
}

func TestAPIKeyFor_MatchesPortAndReturnsKey(t *testing.T) {
	dataDir := withTempInstancesDir(t)
	pid := os.Getpid()
	writeInstanceFile(t, dataDir, "revit-2019-"+itoa(pid)+".json",
		`{"pid":`+itoa(pid)+`,"version":2019,"port":5012,"started_at":"2026-11-17T00:00:00Z","hostname":"localhost","commands_count":99,"api_key":"registry-key"}`)

	if got := APIKeyFor("http://localhost:5012"); got != "registry-key" {
		t.Fatalf("expected 'registry-key' for port 5012, got %q", got)
	}
}

func TestAPIKeyFor_ReturnsEmptyWhenNoInstanceMatches(t *testing.T) {
	dataDir := withTempInstancesDir(t)
	pid := os.Getpid()
	writeInstanceFile(t, dataDir, "revit-2019-"+itoa(pid)+".json",
		`{"pid":`+itoa(pid)+`,"version":2019,"port":5011,"started_at":"2026-11-17T00:00:00Z","hostname":"localhost","commands_count":99,"api_key":"registry-key"}`)

	if got := APIKeyFor("http://localhost:5012"); got != "" {
		t.Fatalf("expected no key for port 5012, got %q", got)
	}
}

func TestAPIKeyFor_IgnoresEntriesWithoutApiKey(t *testing.T) {
	dataDir := withTempInstancesDir(t)
	pid := os.Getpid()
	writeInstanceFile(t, dataDir, "revit-2019-"+itoa(pid)+".json",
		`{"pid":`+itoa(pid)+`,"version":2019,"port":5012,"started_at":"2026-11-17T00:00:00Z","hostname":"localhost","commands_count":99}`)

	if got := APIKeyFor("http://localhost:5012"); got != "" {
		t.Fatalf("expected no key without api_key in the registry file, got %q", got)
	}
}

func TestAPIKeyFor_ReturnsEmptyForURLWithoutPort(t *testing.T) {
	if got := APIKeyFor("http://localhost"); got != "" {
		t.Fatalf("expected empty key for a URL without a port, got %q", got)
	}
}

func itoa(n int) string {
	if n == 0 {
		return "0"
	}
	neg := n < 0
	if neg {
		n = -n
	}
	var buf [20]byte
	i := len(buf)
	for n > 0 {
		i--
		buf[i] = byte('0' + n%10)
		n /= 10
	}
	if neg {
		i--
		buf[i] = '-'
	}
	return string(buf[i:])
}
