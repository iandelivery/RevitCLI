using System.Collections.Generic;
using RevitCliBridge.Abstractions;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for the parameter binding contracts of the cable tray handlers.
    /// Mirrors the POCO shapes declared in the Electrical handler files
    /// (which themselves cannot be linked here because they depend on the
    /// Revit API). Verifies that required fields are enforced, optional
    /// nullable fields default to null, and numeric coercion works for both
    /// long (JSON) and double inputs.
    /// </summary>
    public class CableTrayParamBinderTests
    {
        // ---------- POCOs mirroring the real handler parameter bags ----------

        class CreateCableTrayParams
        {
            [Param("start_x", Required = true)]
            public double StartX { get; set; }

            [Param("start_y", Required = true)]
            public double StartY { get; set; }

            [Param("start_z", Required = true)]
            public double StartZ { get; set; }

            [Param("end_x", Required = true)]
            public double EndX { get; set; }

            [Param("end_y", Required = true)]
            public double EndY { get; set; }

            [Param("end_z", Required = true)]
            public double EndZ { get; set; }

            [Param("level_id", Required = true)]
            public int LevelId { get; set; }

            [Param("type_id")]
            public int? TypeId { get; set; }

            [Param("width_mm")]
            public double WidthMm { get; set; }

            [Param("height_mm")]
            public double HeightMm { get; set; }
        }

        class ModifyCableTrayParams
        {
            [Param("element_id", Required = true)]
            public int ElementId { get; set; }

            [Param("start_x")]
            public double? StartX { get; set; }

            [Param("start_y")]
            public double? StartY { get; set; }

            [Param("start_z")]
            public double? StartZ { get; set; }

            [Param("end_x")]
            public double? EndX { get; set; }

            [Param("end_y")]
            public double? EndY { get; set; }

            [Param("end_z")]
            public double? EndZ { get; set; }

            [Param("type_id")]
            public int? TypeId { get; set; }

            [Param("width_mm")]
            public double? WidthMm { get; set; }

            [Param("height_mm")]
            public double? HeightMm { get; set; }
        }

        class FittingParams
        {
            [Param("element_id_1", Required = true)]
            public int ElementId1 { get; set; }

            [Param("element_id_2", Required = true)]
            public int ElementId2 { get; set; }

            [Param("connector_index_1")]
            public int? ConnectorIndex1 { get; set; }

            [Param("connector_index_2")]
            public int? ConnectorIndex2 { get; set; }
        }

        class TeeParams
        {
            [Param("main_element_id", Required = true)]
            public int MainElementId { get; set; }

            [Param("branch_element_id", Required = true)]
            public int BranchElementId { get; set; }

            [Param("branch_connector_index")]
            public int? BranchConnectorIndex { get; set; }
        }

        class CrossParams
        {
            [Param("main_element_id_1", Required = true)]
            public int MainElementId1 { get; set; }

            [Param("main_element_id_2", Required = true)]
            public int MainElementId2 { get; set; }

            [Param("branch_element_id_1", Required = true)]
            public int BranchElementId1 { get; set; }

            [Param("branch_element_id_2", Required = true)]
            public int BranchElementId2 { get; set; }
        }

        // ---------- create_cable_tray ----------

        [Fact]
        public void CreateCableTray_Bind_RequiredOnly()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["start_z"] = 3000.0,
                ["end_x"] = 5000.0, ["end_y"] = 0.0, ["end_z"] = 3000.0,
                ["level_id"] = 3001L,
            };

            var p = ParameterBinder.Bind<CreateCableTrayParams>(dict);

            Assert.Equal(0.0, p.StartX);
            Assert.Equal(3000.0, p.StartZ);
            Assert.Equal(5000.0, p.EndX);
            Assert.Equal(3001, p.LevelId);
            Assert.Null(p.TypeId);
            Assert.Equal(0.0, p.WidthMm);
            Assert.Equal(0.0, p.HeightMm);
        }

        [Fact]
        public void CreateCableTray_Bind_WithOptionalOverrides()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["start_z"] = 3000.0,
                ["end_x"] = 5000.0, ["end_y"] = 0.0, ["end_z"] = 3000.0,
                ["level_id"] = 3001L,
                ["type_id"] = 12345L,
                ["width_mm"] = 200.0,
                ["height_mm"] = 100.0,
            };

            var p = ParameterBinder.Bind<CreateCableTrayParams>(dict);

            Assert.Equal(12345, p.TypeId);
            Assert.Equal(200.0, p.WidthMm);
            Assert.Equal(100.0, p.HeightMm);
        }

        [Fact]
        public void CreateCableTray_Bind_MissingRequired_Throws()
        {
            var dict = new Dictionary<string, object>
            {
                ["start_x"] = 0.0, ["start_y"] = 0.0, ["start_z"] = 3000.0,
                // end_x, end_y, end_z, level_id missing
            };

            var ex = Assert.Throws<MissingParameterException>(
                () => ParameterBinder.Bind<CreateCableTrayParams>(dict));
            Assert.Equal("end_x", ex.ParameterName);
        }

        // ---------- modify_cable_tray ----------

        [Fact]
        public void ModifyCableTray_Bind_OnlyElementId_Required()
        {
            var dict = new Dictionary<string, object>
            {
                ["element_id"] = 12345L,
            };

            var p = ParameterBinder.Bind<ModifyCableTrayParams>(dict);

            Assert.Equal(12345, p.ElementId);
            Assert.Null(p.StartX);
            Assert.Null(p.EndX);
            Assert.Null(p.TypeId);
            Assert.Null(p.WidthMm);
            Assert.Null(p.HeightMm);
        }

        [Fact]
        public void ModifyCableTray_Bind_PartialUpdate()
        {
            var dict = new Dictionary<string, object>
            {
                ["element_id"] = 12345L,
                ["end_x"] = 6000.0,
                ["width_mm"] = 300.0,
            };

            var p = ParameterBinder.Bind<ModifyCableTrayParams>(dict);

            Assert.Equal(12345, p.ElementId);
            Assert.Null(p.StartX);
            Assert.Equal(6000.0, p.EndX);
            Assert.Equal(300.0, p.WidthMm);
            Assert.Null(p.HeightMm);
        }

        [Fact]
        public void ModifyCableTray_Bind_MissingElementId_Throws()
        {
            var dict = new Dictionary<string, object>
            {
                ["end_x"] = 6000.0,
            };

            var ex = Assert.Throws<MissingParameterException>(
                () => ParameterBinder.Bind<ModifyCableTrayParams>(dict));
            Assert.Equal("element_id", ex.ParameterName);
        }

        // ---------- elbow / transition / union (shared FittingParams shape) ----------

        [Fact]
        public void Fitting_Bind_RequiredElementIds()
        {
            var dict = new Dictionary<string, object>
            {
                ["element_id_1"] = 12345L,
                ["element_id_2"] = 12346L,
            };

            var p = ParameterBinder.Bind<FittingParams>(dict);

            Assert.Equal(12345, p.ElementId1);
            Assert.Equal(12346, p.ElementId2);
            Assert.Null(p.ConnectorIndex1);
            Assert.Null(p.ConnectorIndex2);
        }

        [Fact]
        public void Fitting_Bind_WithConnectorIndices()
        {
            var dict = new Dictionary<string, object>
            {
                ["element_id_1"] = 12345L,
                ["element_id_2"] = 12346L,
                ["connector_index_1"] = 0,
                ["connector_index_2"] = 1,
            };

            var p = ParameterBinder.Bind<FittingParams>(dict);

            Assert.Equal(0, p.ConnectorIndex1);
            Assert.Equal(1, p.ConnectorIndex2);
        }

        [Fact]
        public void Fitting_Bind_MissingSecondElement_Throws()
        {
            var dict = new Dictionary<string, object>
            {
                ["element_id_1"] = 12345L,
            };

            var ex = Assert.Throws<MissingParameterException>(
                () => ParameterBinder.Bind<FittingParams>(dict));
            Assert.Equal("element_id_2", ex.ParameterName);
        }

        // ---------- tee ----------

        [Fact]
        public void Tee_Bind_RequiredIds()
        {
            var dict = new Dictionary<string, object>
            {
                ["main_element_id"] = 100L,
                ["branch_element_id"] = 200L,
            };

            var p = ParameterBinder.Bind<TeeParams>(dict);

            Assert.Equal(100, p.MainElementId);
            Assert.Equal(200, p.BranchElementId);
            Assert.Null(p.BranchConnectorIndex);
        }

        [Fact]
        public void Tee_Bind_MissingMain_Throws()
        {
            var dict = new Dictionary<string, object>
            {
                ["branch_element_id"] = 200L,
            };

            var ex = Assert.Throws<MissingParameterException>(
                () => ParameterBinder.Bind<TeeParams>(dict));
            Assert.Equal("main_element_id", ex.ParameterName);
        }

        // ---------- cross ----------

        [Fact]
        public void Cross_Bind_AllFourRequired()
        {
            var dict = new Dictionary<string, object>
            {
                ["main_element_id_1"] = 100L,
                ["main_element_id_2"] = 101L,
                ["branch_element_id_1"] = 200L,
                ["branch_element_id_2"] = 201L,
            };

            var p = ParameterBinder.Bind<CrossParams>(dict);

            Assert.Equal(100, p.MainElementId1);
            Assert.Equal(101, p.MainElementId2);
            Assert.Equal(200, p.BranchElementId1);
            Assert.Equal(201, p.BranchElementId2);
        }

        [Fact]
        public void Cross_Bind_MissingFourthId_Throws()
        {
            var dict = new Dictionary<string, object>
            {
                ["main_element_id_1"] = 100L,
                ["main_element_id_2"] = 101L,
                ["branch_element_id_1"] = 200L,
                // branch_element_id_2 missing
            };

            var ex = Assert.Throws<MissingParameterException>(
                () => ParameterBinder.Bind<CrossParams>(dict));
            Assert.Equal("branch_element_id_2", ex.ParameterName);
        }
    }
}
