using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCliBridge.Abstractions;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Contract tests for the schema-discovery DTOs. The schema endpoint
    /// (<c>GET /api/commands</c> and <c>GET /api/catalog</c>) is a public
    /// API contract relied on by AI agents and the Go CLI; these tests pin
    /// the JSON field names, default values, and <c>NullValueHandling.Ignore</c>
    /// behavior so a silent rename can't break clients.
    /// </summary>
    public class SchemaDtoContractTests
    {
        // ---------- CommandResponse (re-asserted as a DTO contract) ----------

        [Fact]
        public void CommandResponse_Defaults_AreEmptyStrings()
        {
            var r = new CommandResponse();
            Assert.Equal(string.Empty, r.TaskId);
            Assert.Equal(string.Empty, r.Status);
            Assert.Equal(string.Empty, r.Message);
            Assert.Null(r.Data);
            Assert.Null(r.ErrorDetails);
        }

        // ---------- CommandParamSchema ----------

        [Fact]
        public void CommandParamSchema_Defaults_TypeIsString_RequiredFalse()
        {
            var p = new CommandParamSchema();
            Assert.Equal("string", p.Type);
            Assert.False(p.Required);
            Assert.False(p.Deprecated);
            Assert.False(p.Sensitive);
            Assert.Null(p.Description);
            Assert.Null(p.Default);
            Assert.Null(p.ShortFlag);
            Assert.Null(p.EnumValues);
            Assert.Null(p.Properties);
            Assert.Null(p.Context);
            Assert.Null(p.DeprecationMessage);
        }

        [Fact]
        public void CommandParamSchema_JsonSerialization_UsesExpectedFieldNames()
        {
            var p = new CommandParamSchema
            {
                Name = "level_id",
                Type = "int",
                Required = true,
                Description = "Level id",
                Default = 0,
                ShortFlag = "l",
                EnumValues = new[] { "a", "b" },
                Properties = new CommandParamSchema[0],
                Context = new { hint = "any" },
                Deprecated = true,
                DeprecationMessage = "Use level_name instead.",
                Sensitive = true
            };
            var json = JObject.Parse(JsonConvert.SerializeObject(p));

            Assert.Equal("level_id", json["name"]);
            Assert.Equal("int", json["type"]);
            Assert.Equal(true, json["required"]);
            Assert.Equal("Level id", json["description"]);
            Assert.Equal(0, json["default"]);
            Assert.Equal("l", json["short_flag"]);
            Assert.Equal(new JArray("a", "b"), json["enum_values"]);
            Assert.NotNull(json["properties"]);
            Assert.NotNull(json["context"]);
            Assert.Equal(true, json["deprecated"]);
            Assert.Equal("Use level_name instead.", json["deprecation_message"]);
            Assert.Equal(true, json["sensitive"]);
        }

        [Fact]
        public void CommandParamSchema_OptionalFields_OmittedWhenNull()
        {
            var p = new CommandParamSchema { Name = "x", Type = "string" };
            var json = JsonConvert.SerializeObject(p);
            Assert.Contains("\"name\":\"x\"", json);
            Assert.Contains("\"type\":\"string\"", json);
            Assert.Contains("\"required\":false", json);
            Assert.DoesNotContain("\"description\"", json);
            Assert.DoesNotContain("\"default\"", json);
            Assert.DoesNotContain("\"short_flag\"", json);
            Assert.DoesNotContain("\"enum_values\"", json);
            Assert.DoesNotContain("\"properties\"", json);
            Assert.DoesNotContain("\"context\"", json);
            Assert.DoesNotContain("\"deprecation_message\"", json);
        }

        // ---------- CommandDef ----------

        [Fact]
        public void CommandDef_Defaults_HasEmptyNameAndSupportsDryRunFalse()
        {
            var d = new CommandDef();
            Assert.Equal(string.Empty, d.Name);
            Assert.False(d.SupportsDryRun);
            Assert.Null(d.Version);
            Assert.Null(d.Description);
            Assert.Null(d.Category);
            Assert.Null(d.Aliases);
            Assert.Null(d.DomainPath);
            Assert.Null(d.Parameters);
            Assert.Null(d.Examples);
        }

        [Fact]
        public void CommandDef_OptionalFields_OmittedWhenNull()
        {
            var d = new CommandDef { Name = "create_wall", SupportsDryRun = true };
            var json = JsonConvert.SerializeObject(d);
            Assert.Contains("\"name\":\"create_wall\"", json);
            Assert.Contains("\"supports_dry_run\":true", json);
            Assert.DoesNotContain("\"version\"", json);
            Assert.DoesNotContain("\"description\"", json);
            Assert.DoesNotContain("\"category\"", json);
            Assert.DoesNotContain("\"aliases\"", json);
            Assert.DoesNotContain("\"domain_path\"", json);
            Assert.DoesNotContain("\"parameters\"", json);
            Assert.DoesNotContain("\"examples\"", json);
        }

        // ---------- CommandSchema ----------

        [Fact]
        public void CommandSchema_DefaultVersion_IsTwoZeroZeroZero()
        {
            var s = new CommandSchema();
            Assert.Equal("2.0.0", s.Version);
            Assert.NotNull(s.Commands);
            Assert.Empty(s.Commands);
            Assert.Null(s.ServerInfo);
        }

        [Fact]
        public void CommandSchema_JsonSerialization_CommandsAlwaysPresent()
        {
            var s = new CommandSchema
            {
                Version = "2.1.0",
                Commands = new List<CommandDef> { new CommandDef { Name = "ping" } }
            };
            var json = JObject.Parse(JsonConvert.SerializeObject(s));
            Assert.Equal("2.1.0", json["version"]);
            Assert.NotNull(json["commands"]);
            Assert.Equal("ping", json["commands"][0]["name"]);
            // NullValueHandling.Ignore on ServerInfo — should be omitted.
            Assert.Null(json["server_info"]);
        }

        // ---------- ServerInfo & ServerFeatures ----------

        [Fact]
        public void ServerInfo_Defaults_PortZero()
        {
            var s = new ServerInfo();
            Assert.Equal(0, s.Port);
            Assert.Null(s.BridgeVersion);
            Assert.Null(s.Host);
            Assert.Null(s.Plugins);
            Assert.Null(s.Features);
        }

        [Fact]
        public void ServerInfo_OptionalFields_OmittedWhenNull()
        {
            var s = new ServerInfo { Port = 5041 };
            var json = JsonConvert.SerializeObject(s);
            Assert.Contains("\"port\":5041", json);
            Assert.DoesNotContain("\"bridge_version\"", json);
            Assert.DoesNotContain("\"host\"", json);
            Assert.DoesNotContain("\"plugins\"", json);
            Assert.DoesNotContain("\"features\"", json);
        }

        [Fact]
        public void ServerFeatures_Defaults_AllFlagsFalse()
        {
            var f = new ServerFeatures();
            Assert.False(f.DryRun);
            Assert.False(f.ExecuteRaw);
            Assert.Null(f.OutputFormats);
        }

        [Fact]
        public void ServerFeatures_JsonSerialization_FlagsAlwaysPresent()
        {
            var f = new ServerFeatures { DryRun = true, ExecuteRaw = false };
            var json = JsonConvert.SerializeObject(f);
            Assert.Contains("\"dry_run\":true", json);
            Assert.Contains("\"execute_raw\":false", json);
            Assert.DoesNotContain("\"output_formats\"", json);
        }

        // ---------- CommandCatalog & CatalogEntry ----------

        [Fact]
        public void CommandCatalog_Defaults_HaveEmptyCommandList()
        {
            var c = new CommandCatalog();
            Assert.Equal(string.Empty, c.CatalogVersion);
            Assert.Equal(0, c.CommandCount);
            Assert.NotNull(c.Commands);
            Assert.Empty(c.Commands);
        }

        [Fact]
        public void CommandCatalog_JsonSerialization_FieldsAlwaysPresent()
        {
            var c = new CommandCatalog
            {
                CatalogVersion = "1.0",
                CommandCount = 2,
                Commands = new List<CatalogEntry>
                {
                    new CatalogEntry { Name = "ping", Category = "General", Summary = "Health check" },
                    new CatalogEntry { Name = "create_wall", Category = "Creation" }
                }
            };
            var json = JObject.Parse(JsonConvert.SerializeObject(c));
            Assert.Equal("1.0", json["catalog_version"]);
            Assert.Equal(2, json["command_count"]);
            Assert.Equal("ping", json["commands"][0]["name"]);
            Assert.Equal("General", json["commands"][0]["category"]);
            Assert.Equal("Health check", json["commands"][0]["summary"]);
            // CatalogEntry with null Summary — field should be omitted.
            Assert.Null(json["commands"][1]["summary"]);
        }

        [Fact]
        public void CatalogEntry_Default_NameEmpty_OthersNull()
        {
            var e = new CatalogEntry();
            Assert.Equal(string.Empty, e.Name);
            Assert.Null(e.Category);
            Assert.Null(e.Summary);
        }

        [Fact]
        public void CatalogEntry_RequiredName_OthersIgnoredWhenNull()
        {
            var e = new CatalogEntry { Name = "ping" };
            var json = JsonConvert.SerializeObject(e);
            Assert.Contains("\"name\":\"ping\"", json);
            Assert.DoesNotContain("\"category\"", json);
            Assert.DoesNotContain("\"summary\"", json);
        }

        // ---------- QueuedCommand ----------

        [Fact]
        public void QueuedCommand_Defaults_AllEmptyOrFalse()
        {
            var q = new QueuedCommand();
            Assert.Equal(string.Empty, q.TaskId);
            Assert.Equal(string.Empty, q.Command);
            Assert.Null(q.Parameters);
            Assert.False(q.DryRun);
            Assert.Null(q.RequestId);
        }

        [Fact]
        public void QueuedCommand_JsonSerialization_FieldsPresentWhenSet()
        {
            var q = new QueuedCommand
            {
                TaskId = "t-1",
                Command = "create_wall",
                Parameters = new { level_id = 3 },
                DryRun = true,
                RequestId = "abc-123"
            };
            var json = JObject.Parse(JsonConvert.SerializeObject(q));
            Assert.Equal("t-1", json["task_id"]);
            Assert.Equal("create_wall", json["command"]);
            Assert.NotNull(json["parameters"]);
            Assert.Equal(true, json["dry_run"]);
            Assert.Equal("abc-123", json["request_id"]);
        }

        [Fact]
        public void QueuedCommand_RequestId_OmittedWhenNull()
        {
            var q = new QueuedCommand { TaskId = "t-2", Command = "ping" };
            var json = JsonConvert.SerializeObject(q);
            Assert.Contains("\"task_id\":\"t-2\"", json);
            Assert.Contains("\"command\":\"ping\"", json);
            Assert.Contains("\"dry_run\":false", json);
            Assert.DoesNotContain("\"request_id\"", json);
        }
    }
}
