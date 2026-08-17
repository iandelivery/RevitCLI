using Xunit;

namespace RevitCliBridge.Tests
{
    /// <summary>
    /// xUnit collection that serializes test classes touching shared static
    /// state. <see cref="CliLogger"/> uses process-wide static fields
    /// (<c>_logDirectory</c>, <c>_writer</c>) that cannot be reset between
    /// parallel tests without coordination. Marking the affected test classes
    /// with this collection guarantees they execute sequentially even when
    /// xUnit parallelizes the rest of the assembly.
    /// </summary>
    [CollectionDefinition("StaticStateSerial", DisableParallelization = true)]
    public class StaticStateTestCollection { }
}
