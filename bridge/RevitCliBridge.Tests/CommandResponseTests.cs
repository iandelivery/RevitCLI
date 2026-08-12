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
    }
}
