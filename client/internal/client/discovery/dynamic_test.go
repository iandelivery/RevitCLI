package discovery

import (
	"testing"

	"revit-cli/internal/models"
)

// Tests for DynamicCommand.parseArgs flag matching. Parameter names use
// underscores (min_x) but the documented CLI convention uses dashes
// (--min-x), so both spellings must bind.
func TestParseArgs_underscoreFlagBinds(t *testing.T) {
	d := NewDynamicCommand(sectionBoxDef())
	params := d.parseArgs([]string{"--min_x", "0", "--max_x", "10000"})

	if v, ok := params["min_x"]; !ok || v != 0.0 {
		t.Errorf("min_x: got %v (%T), want 0.0", v, v)
	}
	if v, ok := params["max_x"]; !ok || v != 10000.0 {
		t.Errorf("max_x: got %v (%T), want 10000.0", v, v)
	}
}

func TestParseArgs_dashFlagBinds(t *testing.T) {
	d := NewDynamicCommand(sectionBoxDef())
	params := d.parseArgs([]string{"--min-x", "0", "--max-x", "10000"})

	if v, ok := params["min_x"]; !ok || v != 0.0 {
		t.Errorf("min_x: got %v (%T), want 0.0", v, v)
	}
	if v, ok := params["max_x"]; !ok || v != 10000.0 {
		t.Errorf("max_x: got %v (%T), want 10000.0", v, v)
	}
}

func TestParseArgs_shortFlagBinds(t *testing.T) {
	def := models.CommandDef{
		Name: "example",
		Parameters: []models.CommandParamSchema{
			{Name: "level_id", Type: "int", ShortFlag: "l"},
		},
	}
	params := NewDynamicCommand(def).parseArgs([]string{"-l", "3001"})

	if v, ok := params["level_id"]; !ok || v != 3001 {
		t.Errorf("level_id: got %v (%T), want 3001", v, v)
	}
}

func TestParseArgs_idsShortFlagBindsIntArray(t *testing.T) {
	def := models.CommandDef{
		Name: "set_section_box",
		Parameters: []models.CommandParamSchema{
			{Name: "element_ids", Type: "int[]", ShortFlag: "ids"},
		},
	}
	params := NewDynamicCommand(def).parseArgs([]string{"-ids", "12345,12346,12347"})

	got, ok := params["element_ids"].([]int)
	if !ok {
		t.Fatalf("element_ids: got %v (%T), want []int", params["element_ids"], params["element_ids"])
	}
	want := []int{12345, 12346, 12347}
	if len(got) != len(want) {
		t.Fatalf("element_ids: got %v, want %v", got, want)
	}
	for i := range want {
		if got[i] != want[i] {
			t.Errorf("element_ids[%d]: got %d, want %d", i, got[i], want[i])
		}
	}
}

func TestParseArgs_absentParamsOmitted(t *testing.T) {
	d := NewDynamicCommand(sectionBoxDef())
	params := d.parseArgs([]string{"--min_x", "0"})

	if _, ok := params["max_x"]; ok {
		t.Error("max_x should be absent when flag is not given")
	}
}

func TestParseArgs_boolCoercion(t *testing.T) {
	def := models.CommandDef{
		Name: "set_section_box",
		Parameters: []models.CommandParamSchema{
			{Name: "enable", Type: "bool", Default: true},
		},
	}
	params := NewDynamicCommand(def).parseArgs([]string{"--enable", "false"})

	if v, ok := params["enable"]; !ok || v != false {
		t.Errorf("enable: got %v (%T), want false", v, v)
	}
}

func sectionBoxDef() models.CommandDef {
	return models.CommandDef{
		Name: "set_section_box",
		Parameters: []models.CommandParamSchema{
			{Name: "min_x", Type: "double"},
			{Name: "max_x", Type: "double"},
		},
	}
}
