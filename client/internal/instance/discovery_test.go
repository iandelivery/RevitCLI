package instance

import (
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

// itoa is a local helper to avoid strconv import noise in test cases.
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
