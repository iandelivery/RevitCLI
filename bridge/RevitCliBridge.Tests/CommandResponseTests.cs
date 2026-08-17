using RevitCliBridge.Abstractions;
using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// Tests for CommandResponse factory methods and JSON serialization.
    /// These cover the public contract used by every command handler
    /// to format success/error payloads.
    /// </summary>
    public class CommandResponseTests
    {
        [Fact]
        public void Success_SetsStatusAndMessage()
        {
            var resp = CommandResponse.Success("task-1", new { count = 3 });
            Assert.Equal("task-1", resp.TaskId);
            Assert.Equal("success", resp.Status);
            Assert.Equal("Success", resp.Message);
            Assert.NotNull(resp.Data);
        }

        [Fact]
        public void Error_SetsErrorStatusAndDetails()
        {
            var resp = CommandResponse.Error("task-2", "boom", "stack");
            Assert.Equal("task-2", resp.TaskId);
            Assert.Equal("error", resp.Status);
            Assert.Equal("boom", resp.Message);
            Assert.Equal("stack", resp.ErrorDetails);
        }

        [Fact]
        public void ToJson_IncludesTaskIdAndStatus()
        {
            var resp = CommandResponse.Success("task-3", null, "ok");
            var json = resp.ToJson();
            Assert.Contains("\"task_id\":\"task-3\"", json);
            Assert.Contains("\"status\":\"success\"", json);
            Assert.Contains("\"message\":\"ok\"", json);
        }

        [Fact]
        public void ToJson_OmitsNullDataAndErrorDetails()
        {
            var resp = CommandResponse.Success("task-4", null);
            var json = resp.ToJson();
            // NullValueHandling.Ignore should drop these fields.
            Assert.DoesNotContain("\"data\"", json);
            Assert.DoesNotContain("\"error_details\"", json);
        }

        // ---------- Edge cases: factory defaults ----------

        [Fact]
        public void Success_WithCustomMessage_PropagatesMessage()
        {
            var resp = CommandResponse.Success("t", new { x = 1 }, "Done");
            Assert.Equal("Done", resp.Message);
        }

        [Fact]
        public void Success_WithNullTaskId_AllowsNullAndSerializesAsNull()
        {
            // The factory doesn't null-coerce; TaskId is set directly to
            // null. The property's default ("") is only used when the
            // setter is never called. Verify ToJson still succeeds and
            // produces "task_id":null (the property lacks
            // NullValueHandling.Ignore).
            var resp = CommandResponse.Success(null!, null);
            var json = resp.ToJson();
            Assert.Contains("\"task_id\":null", json);
        }

        [Fact]
        public void Error_WithoutErrorDetails_LeavesErrorDetailsNull()
        {
            var resp = CommandResponse.Error("t", "fail");
            Assert.Null(resp.ErrorDetails);
            // Data is never set by Error — should remain null.
            Assert.Null(resp.Data);
        }

        [Fact]
        public void Error_WithEmptyMessage_PreservesEmptyString()
        {
            var resp = CommandResponse.Error("t", "");
            Assert.Equal("", resp.Message);
            Assert.Equal("error", resp.Status);
        }

        // ---------- Edge cases: JSON serialization ----------

        [Fact]
        public void ToJson_ErrorIncludesErrorDetails_WhenNonNull()
        {
            var resp = CommandResponse.Error("t", "fail", "trace@1");
            var json = resp.ToJson();
            Assert.Contains("\"error_details\":\"trace@1\"", json);
            // Error responses never set Data, so data should be omitted.
            Assert.DoesNotContain("\"data\"", json);
        }

        [Fact]
        public void ToJson_SerializesComplexDataPayload()
        {
            var resp = CommandResponse.Success("t", new
            {
                items = new[] { 1, 2, 3 },
                meta = new { total = 10, page = 1 }
            });
            var json = resp.ToJson();
            Assert.Contains("\"data\":", json);
            Assert.Contains("\"items\":[1,2,3]", json);
            Assert.Contains("\"meta\":", json);
            Assert.Contains("\"total\":10", json);
            Assert.Contains("\"page\":1", json);
        }

        [Fact]
        public void ToJson_ProducesStableKeyOrdering()
        {
            // Newtonsoft serializes properties in declaration order; for the
            // CommandResponse class, the expected order is task_id, status,
            // message, data, error_details. Verify the serialized string
            // respects this ordering for client compatibility.
            var resp = CommandResponse.Success("t", new { x = 1 }, "m");
            var json = resp.ToJson();

            int idxTaskId = json.IndexOf("\"task_id\"");
            int idxStatus = json.IndexOf("\"status\"");
            int idxMessage = json.IndexOf("\"message\"");
            int idxData = json.IndexOf("\"data\"");

            Assert.True(idxTaskId < idxStatus, "task_id must precede status");
            Assert.True(idxStatus < idxMessage, "status must precede message");
            Assert.True(idxMessage < idxData, "message must precede data");
        }

        [Fact]
        public void ToJson_RoundTripsThroughDeserialization()
        {
            // Verify the JSON can be deserialized back to a CommandResponse
            // with matching field values.
            var original = CommandResponse.Error("t-rt", "fail", "details-here");
            var json = original.ToJson();
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<CommandResponse>(json);

            Assert.NotNull(deserialized);
            Assert.Equal("t-rt", deserialized!.TaskId);
            Assert.Equal("error", deserialized.Status);
            Assert.Equal("fail", deserialized.Message);
            Assert.Equal("details-here", deserialized.ErrorDetails);
        }
    }
}
