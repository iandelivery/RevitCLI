package abstractions

import (
	"testing"
)

func TestFindArg_FindsLongFlag(t *testing.T) {
	args := []string{"--name", "wall"}
	val, ok := FindArg(args, "--name", "-n")
	if !ok {
		t.Fatal("expected ok=true")
	}
	if val != "wall" {
		t.Fatalf("expected 'wall', got %q", val)
	}
}

func TestFindArg_FindsShortAlias(t *testing.T) {
	args := []string{"-n", "wall"}
	val, ok := FindArg(args, "--name", "-n")
	if !ok {
		t.Fatal("expected ok=true for short alias")
	}
	if val != "wall" {
		t.Fatalf("expected 'wall', got %q", val)
	}
}

func TestFindArg_ReturnsFalseWhenMissing(t *testing.T) {
	args := []string{"--other", "x"}
	_, ok := FindArg(args, "--name", "-n")
	if ok {
		t.Fatal("expected ok=false when flag is absent")
	}
}

func TestFindArg_RejectsFlagLikeValue(t *testing.T) {
	// --name --other should NOT consume "--other" as the value of --name.
	args := []string{"--name", "--other", "value"}
	_, ok := FindArg(args, "--name")
	if ok {
		t.Fatal("expected ok=false when next arg looks like a flag")
	}
}

func TestFindArg_ReturnsFalseWhenFlagIsLastArg(t *testing.T) {
	args := []string{"--name"}
	_, ok := FindArg(args, "--name")
	if ok {
		t.Fatal("expected ok=false when flag has no value")
	}
}

func TestHasFlag_DetectsPresence(t *testing.T) {
	args := []string{"--json", "extra"}
	if !HasFlag(args, "--json") {
		t.Fatal("expected HasFlag=true for --json")
	}
	if HasFlag(args, "--xml") {
		t.Fatal("expected HasFlag=false for absent --xml")
	}
}

func TestHasFlag_AcceptsMultipleAliases(t *testing.T) {
	args := []string{"-j"}
	if !HasFlag(args, "--json", "-j") {
		t.Fatal("expected HasFlag=true via -j alias")
	}
}

func TestGetInt_ParsesValidInteger(t *testing.T) {
	args := []string{"--count", "42"}
	n, ok := GetInt(args, "--count", "-c")
	if !ok {
		t.Fatal("expected ok=true")
	}
	if n != 42 {
		t.Fatalf("expected 42, got %d", n)
	}
}

func TestGetInt_ReturnsFalseForNonNumeric(t *testing.T) {
	args := []string{"--count", "abc"}
	_, ok := GetInt(args, "--count")
	if ok {
		t.Fatal("expected ok=false for non-numeric value")
	}
}

func TestGetDouble_ParsesFloat(t *testing.T) {
	args := []string{"--offset", "1.5"}
	d, ok := GetDouble(args, "--offset")
	if !ok {
		t.Fatal("expected ok=true")
	}
	if d != 1.5 {
		t.Fatalf("expected 1.5, got %f", d)
	}
}

func TestTryParseValue_IntFirst(t *testing.T) {
	v := TryParseValue("42")
	i, ok := v.(int)
	if !ok {
		t.Fatalf("expected int, got %T", v)
	}
	if i != 42 {
		t.Fatalf("expected 42, got %d", i)
	}
}

func TestTryParseValue_FloatSecond(t *testing.T) {
	v := TryParseValue("3.14")
	d, ok := v.(float64)
	if !ok {
		t.Fatalf("expected float64, got %T", v)
	}
	if d != 3.14 {
		t.Fatalf("expected 3.14, got %f", d)
	}
}

func TestTryParseValue_FallsBackToString(t *testing.T) {
	v := TryParseValue("hello")
	s, ok := v.(string)
	if !ok {
		t.Fatalf("expected string, got %T", v)
	}
	if s != "hello" {
		t.Fatalf("expected 'hello', got %q", s)
	}
}

func TestParseIDs_HandlesCommaSeparated(t *testing.T) {
	ids := ParseIDs("1,2,3,4")
	expected := []int{1, 2, 3, 4}
	if len(ids) != len(expected) {
		t.Fatalf("expected %v, got %v", expected, ids)
	}
	for i, id := range ids {
		if id != expected[i] {
			t.Fatalf("at index %d: expected %d, got %d", i, expected[i], id)
		}
	}
}

func TestParseIDs_HandlesWhitespace(t *testing.T) {
	ids := ParseIDs(" 1 , 2 , 3 ")
	if len(ids) != 3 || ids[0] != 1 || ids[1] != 2 || ids[2] != 3 {
		t.Fatalf("expected [1 2 3], got %v", ids)
	}
}

func TestParseIDs_ReturnsNilForInvalid(t *testing.T) {
	if ids := ParseIDs("1,abc,3"); ids != nil {
		t.Fatalf("expected nil for invalid segment, got %v", ids)
	}
}

func TestParseIDs_ReturnsNilForEmpty(t *testing.T) {
	if ids := ParseIDs(""); ids != nil {
		t.Fatalf("expected nil for empty input, got %v", ids)
	}
}
