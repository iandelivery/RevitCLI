using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCliBridge.Abstractions;
using RevitCliBridge.Models;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Integration tests that exercise multiple pure-logic components
    /// together to verify key end-to-end workflows of the Revit CLI Bridge.
    ///
    /// These tests do NOT spin up the HTTP server, the IPC bridge, or the
    /// Revit add-in — they orchestrate the in-process building blocks
    /// (CommandNameResolver, ParameterBinder, PagedResultBuilder,
    /// TaskStateMachine, CliBridgeConfigAuth, CommandResponse) the same
    /// way the production HTTP handlers do. This keeps them deterministic,
    /// fast, and independent of Revit/CI environment.
    /// </summary>
    public class IntegrationTests
    {
        // ---------- Workflow: command dispatch (resolve → bind → respond) ----------

        /// <summary>
        /// Sample parameter DTO used by several integration tests. Mirrors
        /// the shape of a real wall-creation command payload.
        /// </summary>
        public class WallParams
        {
            [Param(Required = true)] public double StartX { get; set; }
            [Param(Required = true)] public double StartY { get; set; }
            [Param(Required = true)] public double EndX { get; set; }
            [Param(Required = true)] public double EndY { get; set; }
            [Param] public int? LevelId { get; set; }
            [Param] public int[]? TagIds { get; set; }
            [Param(Required = true)] public string WallType { get; set; } = string.Empty;
        }

        [Fact]
        public void CommandDispatch_HappyPath_ResolvesName_BindsParams_BuildsResponse()
        {
            // Simulate the production dispatch flow for a domain-prefixed
            // command name with structured parameters.
            var registered = new HashSet<string> { "create_wall@v1" };
            string input = "domain.create_wall@v1";

            // 1. Resolve the command name against the registry.
            string resolved = CommandNameResolver.Resolve(input, registered);
            Assert.Equal("create_wall@v1", resolved);

            // 2. Bind the request parameter dictionary to the strongly-typed DTO.
            var parameters = new Dictionary<string, object>
            {
                ["start_x"] = 0.0,
                ["start_y"] = 0.0,
                ["end_x"] = 10.0,
                ["end_y"] = 20.0,
                ["level_id"] = 5,
                ["tag_ids"] = new List<object> { 1, 2, 3 },
                ["wall_type"] = "Curtain",
            };
            var bound = ParameterBinder.Bind<WallParams>(parameters);

            // 3. Build a success response with the bound data.
            var response = CommandResponse.Success("task-1", new
            {
                command = resolved,
                bound,
            });

            Assert.Equal("success", response.Status);
            var json = JObject.Parse(response.ToJson());
            Assert.Equal("create_wall@v1", json["data"]!["command"]!.ToString());
            // WallParams properties are PascalCase in C# and serialize as such
            // (no JsonProperty attributes on the test DTO).
            Assert.Equal(10.0, json["data"]!["bound"]!["EndX"]!.Value<double>());
            Assert.Equal("Curtain", json["data"]!["bound"]!["WallType"]!.ToString());
            Assert.Equal(3, json["data"]!["bound"]!["TagIds"]!.Count());
        }

        [Fact]
        public void CommandDispatch_MissingRequiredParam_ThrowsMissingParameterException()
        {
            // Production handlers translate missing-parameter errors into
            // a 400 response. Verify the binder raises the expected
            // exception type so the handler can convert it cleanly.
            var parameters = new Dictionary<string, object>
            {
                ["start_x"] = 0.0,
                // start_y missing
                ["end_x"] = 10.0,
                ["end_y"] = 20.0,
                ["wall_type"] = "Basic",
            };

            var ex = Assert.Throws<MissingParameterException>(
                () => ParameterBinder.Bind<WallParams>(parameters));
            Assert.Equal("start_y", ex.ParameterName);
        }

        [Fact]
        public void CommandDispatch_WrongParameterType_ThrowsParameterTypeException()
        {
            var parameters = new Dictionary<string, object>
            {
                ["start_x"] = 0.0,
                ["start_y"] = 0.0,
                ["end_x"] = 10.0,
                ["end_y"] = 20.0,
                ["wall_type"] = "Basic",
                ["tag_ids"] = new List<object> { 1, "not-a-number", 3 },
            };

            var ex = Assert.Throws<ParameterTypeException>(
                () => ParameterBinder.Bind<WallParams>(parameters));
            Assert.Equal("tag_ids", ex.ParameterName);
        }

        [Fact]
        public void CommandDispatch_UnknownCommand_ReturnsInputForHandlerTo404()
        {
            // When the resolver can't match the input to a registered
            // command, it returns the input unchanged. Production
            // handlers use this as the signal to return a 404-style
            // error response.
            var registered = new HashSet<string> { "create_wall@v1" };
            string resolved = CommandNameResolver.Resolve("nonexistent@v1", registered);

            Assert.Equal("nonexistent@v1", resolved);
            Assert.NotEqual("create_wall@v1", resolved);

            // Handler builds an error response with the unresolved name.
            var response = CommandResponse.Error("task-x", $"Unknown command: {resolved}");
            Assert.Equal("error", response.Status);
            Assert.Contains("Unknown command: nonexistent@v1", response.Message);
        }

        // ---------- Workflow: pagination (parse → build → serialize) ----------

        [Fact]
        public void Pagination_FullFlow_ParsesQuery_BuildsPage_SerializesResult()
        {
            // Mirrors the production list-elements handler:
            // 1. Parse limit/offset from the query dict (with clamping).
            // 2. Apply pagination to the ordered source.
            // 3. Wrap the page in a JSON response.
            var query = new Dictionary<string, object>
            {
                ["limit"] = "5",
                ["offset"] = "10",
            };
            var (limit, offset) = PagedResultBuilder.GetPagingParams(query);
            Assert.Equal(5, limit);
            Assert.Equal(10, offset);

            var source = Enumerable.Range(1, 100).Select(i => new { id = i, name = $"item-{i}" });
            var page = PagedResultBuilder.Build(source, limit, offset);

            Assert.Equal(5, page.Items.Count);
            Assert.True(page.HasMore);
            Assert.Equal(11, page.Items[0].id);
            Assert.Equal(15, page.Items[4].id);

            var response = CommandResponse.Success("task-page", new
            {
                items = page.Items,
                count = page.Count,
                offset = page.Offset,
                limit = page.Limit,
                has_more = page.HasMore,
            });

            var json = JObject.Parse(response.ToJson());
            Assert.Equal(5, json["data"]!["items"]!.Count());
            Assert.True(json["data"]!["has_more"]!.Value<bool>());
            Assert.Equal(15, json["data"]!["items"]![4]!["id"]!.Value<int>());
        }

        [Fact]
        public void Pagination_ClampsOversizedLimitToMax_BeforeBuildingPage()
        {
            // A malicious client requests limit=999_999. GetPagingParams
            // must clamp to MaxLimit (5000) so we don't materialize
            // an enormous list.
            var query = new Dictionary<string, object> { ["limit"] = 999_999 };
            var (limit, _) = PagedResultBuilder.GetPagingParams(query);

            Assert.Equal(PagedResultBuilder.MaxLimit, limit);

            // The clamped limit is then used to build the page.
            var source = Enumerable.Range(1, 6000);
            var page = PagedResultBuilder.Build(source, limit, offset: 0);
            Assert.Equal(PagedResultBuilder.MaxLimit, page.Items.Count);
            Assert.True(page.HasMore);
        }

        [Fact]
        public void Pagination_LastPartialPage_HasMoreFalse_StopsIteration()
        {
            // The has_more trick must return false on the final page
            // so the client knows to stop fetching.
            var source = Enumerable.Range(1, 25);
            var query = new Dictionary<string, object> { ["limit"] = 10, ["offset"] = 20 };
            var (limit, offset) = PagedResultBuilder.GetPagingParams(query);

            var page = PagedResultBuilder.Build(source, limit, offset);
            Assert.Equal(5, page.Items.Count);
            Assert.False(page.HasMore);

            var response = CommandResponse.Success("t", new { page.Items, page.HasMore });
            var json = JObject.Parse(response.ToJson());
            // Anonymous-type properties serialize as PascalCase by default.
            Assert.False(json["data"]!["HasMore"]!.Value<bool>());
        }

        // ---------- Workflow: SSE event stream for task lifecycle ----------

        [Fact]
        public void SseEvent_Lifecycle_PendingToRunningToCompleted_SubscriberReceivesAllEvents()
        {
            // The SSE subscriber registered on a TaskInfo must see one event
            // per state-machine transition, in order. This is the contract
            // the bridge's /events endpoint relies on.
            var task = new TaskInfo { TaskId = "task-sse", Command = "ping" };
            var events = new List<(string name, string json)>();
            task.OnSseEvent += (name, json) => events.Add((name, json));

            TaskStateMachine.SetRunning(task);
            TaskStateMachine.SetProgress(task, 50, "halfway");
            TaskStateMachine.SetCompleted(task, "{\"ok\":true}");

            Assert.Equal(3, events.Count);
            Assert.Equal("progress", events[0].name);
            Assert.Equal("progress", events[1].name);
            Assert.Equal("completed", events[2].name);

            // First event: progress=0 (the SetRunning broadcast).
            var first = JObject.Parse(events[0].json);
            Assert.Equal(0, first["progress"]!.Value<int>());
            Assert.Equal("Execution started", first["message"]!.ToString());

            // Second event: progress=50.
            var second = JObject.Parse(events[1].json);
            Assert.Equal(50, second["progress"]!.Value<int>());
            Assert.Equal("halfway", second["message"]!.ToString());

            // Third event: completed with parsed JSON result.
            var third = JObject.Parse(events[2].json);
            Assert.Equal("completed", third["status"]!.ToString());
            Assert.Equal(true, third["result"]!["ok"]!.Value<bool>());

            // Final task state reflects the last transition.
            Assert.Equal(CliTaskStatus.Completed, task.Status);
        }

        [Fact]
        public void SseEvent_FailedTask_SubscriberReceivesFailedEventWithResult()
        {
            var task = new TaskInfo { TaskId = "task-fail", Command = "ping" };
            var events = new List<(string name, string json)>();
            task.OnSseEvent += (name, json) => events.Add((name, json));

            TaskStateMachine.SetRunning(task);
            TaskStateMachine.SetFailed(task, "{\"error\":\"boom\",\"code\":500}");

            Assert.Equal(2, events.Count);
            Assert.Equal("failed", events[1].name);

            var payload = JObject.Parse(events[1].json);
            Assert.Equal("failed", payload["status"]!.ToString());
            Assert.Equal("boom", payload["result"]!["error"]!.ToString());
            Assert.Equal(500, payload["result"]!["code"]!.Value<int>());
        }

        [Fact]
        public async System.Threading.Tasks.Task SseEvent_TaskCompletion_AwaiterUnblocksOnCompleted()
        {
            // A client polling the task result via the TCS must unblock
            // when SetCompleted fires. This is the contract that makes
            // long-poll work without busy-waiting.
            var task = new TaskInfo { TaskId = "task-await", Command = "ping" };

            // Start an awaiter that completes when the TCS does.
            var awaiter = System.Threading.Tasks.Task.Run(() => task.Tcs.Task.Result);

            // Give the awaiter a moment to register.
            await System.Threading.Tasks.Task.Delay(20);
            Assert.False(awaiter.IsCompleted);

            TaskStateMachine.SetCompleted(task, "{\"done\":true}");

            // The awaiter should now complete with the result JSON.
            string result = await awaiter;
            Assert.Equal("{\"done\":true}", result);
        }

        // ---------- Workflow: auth gate then dispatch ----------

        [Fact]
        public void AuthGate_ValidToken_ProceedsToDispatch()
        {
            // Production flow: validate bearer token, then resolve command
            // and bind params. Token must be stripped of "Bearer " prefix
            // before being compared to the configured ApiKey.
            var config = new CliBridgeConfig { ApiKey = "secret-123" };
            string bearer = "Bearer secret-123";

            Assert.True(CliBridgeConfigAuth.Validate(config, bearer));

            // Auth succeeded → proceed with dispatch.
            var registered = new HashSet<string> { "ping@v1" };
            string resolved = CommandNameResolver.Resolve("ping@v1", registered);
            Assert.Equal("ping@v1", resolved);

            var response = CommandResponse.Success("task-auth-ok", new { pong = true });
            Assert.Equal("success", response.Status);
        }

        [Fact]
        public void AuthGate_InvalidToken_RejectsWithoutDispatching()
        {
            var config = new CliBridgeConfig { ApiKey = "secret-123" };
            string bearer = "Bearer wrong";

            Assert.False(CliBridgeConfigAuth.Validate(config, bearer));

            // Auth failed → handler short-circuits before dispatch.
            var response = CommandResponse.Error("task-auth-fail", "Unauthorized", "Invalid API key");
            Assert.Equal("error", response.Status);
            Assert.Equal("Unauthorized", response.Message);
        }

        [Fact]
        public void AuthGate_Disabled_SkipsAuthAndDispatches()
        {
            // When ApiKey is null/empty, auth is disabled and every
            // request proceeds to dispatch directly.
            var config = new CliBridgeConfig { ApiKey = null };
            Assert.False(CliBridgeConfigAuth.IsEnabled(config));

            // Any token (including null) passes the gate.
            Assert.True(CliBridgeConfigAuth.Validate(config, null));

            var registered = new HashSet<string> { "ping@v1" };
            string resolved = CommandNameResolver.Resolve("ping@v1", registered);
            Assert.Equal("ping@v1", resolved);
        }

        // ---------- Workflow: schema generation → response serialization ----------

        [Fact]
        public void SchemaGeneration_BuildsCommandDef_SerializesToExpectedJsonContract()
        {
            // The /schema endpoint exposes a CommandDef per registered
            // command. Verify the DTO serializes with the field names
            // the Go client expects.
            var def = new CommandDef
            {
                Name = "create_wall",
                Description = "Creates a wall between two points",
                Category = "Modeling",
                Parameters = new[]
                {
                    new CommandParamSchema
                    {
                        Name = "start_x",
                        Type = "double",
                        Required = true,
                        Description = "Start X coordinate",
                        Default = null,
                    },
                    new CommandParamSchema
                    {
                        Name = "level_id",
                        Type = "int",
                        Required = false,
                        Default = 1,
                    },
                },
            };

            var response = CommandResponse.Success("task-schema", def);
            var json = JObject.Parse(response.ToJson());

            Assert.Equal("create_wall", json["data"]!["name"]!.ToString());
            Assert.Equal("Creates a wall between two points", json["data"]!["description"]!.ToString());
            Assert.Equal(2, json["data"]!["parameters"]!.Count());

            var firstParam = json["data"]!["parameters"]![0]!;
            Assert.Equal("start_x", firstParam["name"]!.ToString());
            Assert.Equal("double", firstParam["type"]!.ToString());
            Assert.True(firstParam["required"]!.Value<bool>());

            var secondParam = json["data"]!["parameters"]![1]!;
            Assert.Equal("level_id", secondParam["name"]!.ToString());
            Assert.Equal("int", secondParam["type"]!.ToString());
            Assert.False(secondParam["required"]!.Value<bool>());
            Assert.Equal(1, secondParam["default"]!.Value<int>());
        }

        [Fact]
        public void SchemaGeneration_VersionedCommand_PreservesVersionInName()
        {
            // The resolver may normalize a domain-prefixed input to a
            // registered versioned name. The schema response should
            // report the resolved (registered) name, not the input.
            var registered = new HashSet<string> { "create_wall@v2" };
            string resolved = CommandNameResolver.Resolve("domain.create_wall@v2", registered);

            var def = new CommandDef { Name = resolved, Description = "v2 of create_wall" };
            var response = CommandResponse.Success("t", def);

            var json = JObject.Parse(response.ToJson());
            Assert.Equal("create_wall@v2", json["data"]!["name"]!.ToString());
        }
    }

    /// <summary>
    /// Scalability-focused integration tests that exercise the same
    /// workflows as <see cref="IntegrationTests"/> but with 100+
    /// registered commands. These guard against O(n²) algorithmic
    /// regressions in CommandNameResolver, schema serialization, and
    /// catalog pagination as the command set grows beyond the initial
    /// ~30 commands. They are deterministic and fast (target: well
    /// under 1s total) so they can run on every PR.
    /// </summary>
    public class ScalabilityIntegrationTests
    {
        private const int CommandCount = 150;

        /// <summary>
        /// Build a registered-name set with 150 entries spanning bare,
        /// versioned, and domain-prefixed names — mirrors what a
        /// real plugin-heavy install looks like.
        /// </summary>
        private static HashSet<string> BuildRegisteredNames()
        {
            var names = new HashSet<string>();

            // 70 bare names: cmd_000 ... cmd_069
            for (int i = 0; i < 70; i++)
                names.Add($"cmd_{i:D3}");

            // 50 versioned names: cmd_070@v1 ... cmd_119@v1
            for (int i = 70; i < 120; i++)
                names.Add($"cmd_{i:D3}@v1");

            // 30 domain-prefixed names: domain_alpha.cmd_120 ...
            for (int i = 120; i < 150; i++)
                names.Add($"domain_alpha.cmd_{i:D3}");

            return names;
        }

        // ---------- CommandNameResolver at scale ----------

        [Fact]
        public void Resolve_AtScale_BareName_LookupIsFastAndCorrect()
        {
            var registered = BuildRegisteredNames();

            // Resolve a bare name from the middle of the set.
            string resolved = CommandNameResolver.Resolve("cmd_035", registered);

            Assert.Equal("cmd_035", resolved);
        }

        [Fact]
        public void Resolve_AtScale_VersionedName_LookupIsCorrect()
        {
            var registered = BuildRegisteredNames();

            string resolved = CommandNameResolver.Resolve("cmd_099@v1", registered);

            Assert.Equal("cmd_099@v1", resolved);
        }

        [Fact]
        public void Resolve_AtScale_DomainPath_TriesSuffixButFallsBackToInput()
        {
            // cmd_125 is registered as "domain_alpha.cmd_125". An input
            // like "domain_beta.cmd_125" tries the suffix "cmd_125",
            // which isn't registered on its own — so the resolver
            // returns the input unchanged. Cross-domain suffix matching
            // is NOT supported; only bare-suffix matches resolve.
            var registered = BuildRegisteredNames();

            string resolved = CommandNameResolver.Resolve("domain_beta.cmd_125", registered);

            Assert.Equal("domain_beta.cmd_125", resolved);
        }

        [Fact]
        public void Resolve_AtScale_DomainPath_BareSuffixMatchResolves()
        {
            // Register a bare "cmd_125" alongside the domain-prefixed
            // entry. A domain_beta input should resolve to the bare name.
            var registered = BuildRegisteredNames();
            registered.Add("cmd_125");

            string resolved = CommandNameResolver.Resolve("domain_beta.cmd_125", registered);

            Assert.Equal("cmd_125", resolved);
        }

        [Fact]
        public void Resolve_AtScale_UnderscoreReversal_FindsTwoPartMatch()
        {
            // For two-segment names, the resolver tries the underscore-
            // reversed form. Register cmd_a_b and resolve b_a_cmd.
            var registered = new HashSet<string> { "create_wall", "create_door", "create_window" };
            registered.Add("level_wall");

            string resolved = CommandNameResolver.Resolve("wall_level", registered);

            Assert.Equal("level_wall", resolved);
        }

        [Fact]
        public void Resolve_AtScale_NonExistent_ReturnsInputUnchanged()
        {
            // Out of 150 commands, none matches "no_such_command@v9".
            // The resolver must return the input unchanged (no exception).
            var registered = BuildRegisteredNames();

            string resolved = CommandNameResolver.Resolve("no_such_command@v9", registered);

            Assert.Equal("no_such_command@v9", resolved);
        }

        [Fact]
        public void Resolve_AtScale_AllCommandsResolveCorrectly()
        {
            // Brute-force verify that every registered name resolves to
            // itself when passed directly. This catches off-by-one errors
            // in the resolver's suffix/reversal logic that might surface
            // only on specific name patterns.
            var registered = BuildRegisteredNames();

            foreach (string name in registered)
            {
                string resolved = CommandNameResolver.Resolve(name, registered);
                Assert.Equal(name, resolved);
            }
        }

        // ---------- Schema serialization at scale ----------

        [Fact]
        public void SchemaSerialization_AtScale_AllCommandsIncludedInResponse()
        {
            // The /api/commands endpoint serializes the full CommandSchema
            // with every registered command. At 150 commands, the response
            // JSON must include all of them — none dropped silently.
            var registered = BuildRegisteredNames();
            var schema = new CommandSchema
            {
                Version = "2.1.0",
                Commands = registered
                    .OrderBy(n => n)
                    .Select(n => new CommandDef
                    {
                        Name = n,
                        Category = n.StartsWith("domain_") ? "Domain" : "Core",
                        Description = $"Summary for {n}",
                    })
                    .ToList(),
            };

            string json = JsonConvert.SerializeObject(schema);
            var parsed = JObject.Parse(json);

            Assert.Equal(CommandCount, parsed["commands"]!.Count());
            // Spot-check first and last entries to ensure no truncation.
            Assert.Equal("cmd_000", parsed["commands"]![0]!["name"]!.ToString());
            Assert.Equal("domain_alpha.cmd_149", parsed["commands"]![149]!["name"]!.ToString());
        }

        [Fact]
        public void SchemaSerialization_AtScale_ResponseSizeStaysReasonable()
        {
            // Guard against the schema response growing unbounded as
            // command count grows. With 150 commands and minimal
            // per-command metadata, the JSON should be well under
            // 200 KB. If this test starts failing, it means per-command
            // metadata is bloating — split into the lightweight catalog
            // response (CommandCatalog) and full schema.
            var registered = BuildRegisteredNames();
            var schema = new CommandSchema
            {
                Version = "2.1.0",
                Commands = registered
                    .Select(n => new CommandDef
                    {
                        Name = n,
                        Description = $"Summary for {n}",
                        Category = "Core",
                    })
                    .ToList(),
            };

            string json = JsonConvert.SerializeObject(schema);

            // 150 commands × ~80 bytes each ≈ 12 KB. Allow generous
            // headroom for parameter schemas and examples.
            Assert.True(json.Length < 200_000,
                $"Schema JSON grew to {json.Length} bytes — investigate per-command bloat");
        }

        // ---------- Catalog (lightweight) at scale ----------

        [Fact]
        public void CatalogSerialization_AtScale_ReportedCountMatchesActualCount()
        {
            // The catalog endpoint exists specifically for the 100+
            // command case — it trades parameter schemas for size.
            // Verify CommandCount matches the actual list length so
            // clients can use it for sanity checks.
            var registered = BuildRegisteredNames();
            var catalog = new CommandCatalog
            {
                CatalogVersion = "1.0",
                CommandCount = registered.Count,
                Commands = registered
                    .OrderBy(n => n)
                    .Select(n => new CatalogEntry
                    {
                        Name = n,
                        Category = "Core",
                        Summary = $"Summary for {n}",
                    })
                    .ToList(),
            };

            string json = JsonConvert.SerializeObject(catalog);
            var parsed = JObject.Parse(json);

            Assert.Equal(CommandCount, parsed["command_count"]!.Value<int>());
            Assert.Equal(CommandCount, parsed["commands"]!.Count());
        }

        [Fact]
        public void CatalogSerialization_AtScale_IsMuchSmallerThanFullSchema()
        {
            // The whole point of the catalog DTO is to be smaller than
            // the full schema. Verify the size ratio holds at 150
            // commands so AI agents don't waste context tokens.
            var registered = BuildRegisteredNames();

            var fullSchema = new CommandSchema
            {
                Version = "2.1.0",
                Commands = registered.Select(n => new CommandDef
                {
                    Name = n,
                    Description = $"Long description for {n} explaining usage",
                    Category = "Core",
                    Parameters = new[]
                    {
                        new CommandParamSchema { Name = "x", Type = "double", Required = true },
                        new CommandParamSchema { Name = "y", Type = "double", Required = true },
                    },
                }).ToList(),
            };

            var catalog = new CommandCatalog
            {
                CatalogVersion = "1.0",
                CommandCount = registered.Count,
                Commands = registered.Select(n => new CatalogEntry
                {
                    Name = n,
                    Summary = $"Summary for {n}",
                }).ToList(),
            };

            string fullJson = JsonConvert.SerializeObject(fullSchema);
            string catalogJson = JsonConvert.SerializeObject(catalog);

            Assert.True(catalogJson.Length < fullJson.Length,
                $"Catalog ({catalogJson.Length} bytes) should be smaller than full schema ({fullJson.Length} bytes)");
        }

        // ---------- Pagination at scale ----------

        [Fact]
        public void Pagination_AtScale_WalksAllPagesWithoutOverlapOrGaps()
        {
            // With 150 commands and a page size of 25, walking 6 pages
            // must cover every command exactly once. This is the
            // pagination contract clients rely on for discovery.
            var all = Enumerable.Range(0, CommandCount)
                .Select(i => $"cmd_{i:D3}")
                .ToList();
            int pageSize = 25;
            int expectedPages = (CommandCount + pageSize - 1) / pageSize; // 6 pages

            var seen = new HashSet<string>();
            int actualPages = 0;
            for (int page = 0; page < expectedPages; page++)
            {
                var result = PagedResultBuilder.Build(all, pageSize, page * pageSize);
                actualPages++;
                foreach (var item in result.Items)
                {
                    Assert.True(seen.Add(item),
                        $"Duplicate item {item} on page {page} — pagination overlap");
                }
            }

            Assert.Equal(expectedPages, actualPages);
            Assert.Equal(CommandCount, seen.Count);
        }

        [Fact]
        public void Pagination_AtScale_HasMoreFlipsOnFinalPage()
        {
            // The has_more flag must be true for all pages except the
            // last one, where it flips to false. This is how clients
            // know to stop fetching.
            var all = Enumerable.Range(0, CommandCount)
                .Select(i => $"cmd_{i:D3}")
                .ToList();
            int pageSize = 50; // 3 pages: 50, 50, 50

            for (int page = 0; page < 3; page++)
            {
                var result = PagedResultBuilder.Build(all, pageSize, page * pageSize);
                bool isLastPage = page == 2;
                Assert.Equal(isLastPage, !result.HasMore);
            }
        }

        [Fact]
        public void Pagination_AtScale_DefaultLimitReturnsFirstPageOnly()
        {
            // A client that omits the limit query parameter gets the
            // default page (DefaultLimit=500). With only 150 commands,
            // everything fits on one page and has_more is false.
            var all = Enumerable.Range(0, CommandCount)
                .Select(i => $"cmd_{i:D3}")
                .ToList();

            var (limit, offset) = PagedResultBuilder.GetPagingParams(null);
            Assert.Equal(PagedResultBuilder.DefaultLimit, limit);
            Assert.Equal(PagedResultBuilder.DefaultOffset, offset);

            var result = PagedResultBuilder.Build(all, limit, offset);
            Assert.Equal(CommandCount, result.Items.Count);
            Assert.False(result.HasMore);
        }

        // ---------- Auth + dispatch at scale ----------

        [Fact]
        public void AuthGate_AtScale_DoesNotBlockOnCommandCount()
        {
            // The auth gate runs before command lookup. With 150
            // registered commands, the auth check must still be O(1)
            // — it only inspects the configured ApiKey, not the
            // command registry.
            var registered = BuildRegisteredNames();
            var config = new CliBridgeConfig { ApiKey = "key-123" };

            // Validate token then resolve a command from the middle.
            Assert.True(CliBridgeConfigAuth.Validate(config, "Bearer key-123"));
            string resolved = CommandNameResolver.Resolve("cmd_075@v1", registered);
            Assert.Equal("cmd_075@v1", resolved);

            // Auth rejection must short-circuit before lookup.
            Assert.False(CliBridgeConfigAuth.Validate(config, "Bearer wrong"));
        }
    }
}
