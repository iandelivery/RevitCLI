namespace RevitCliBridge
{
    /// <summary>
    /// Status of a CLI bridge task. Named CliTaskStatus to avoid conflict
    /// with System.Threading.Tasks.TaskStatus.
    /// </summary>
    public enum CliTaskStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Timeout
    }

    /// <summary>
    /// Backward-compatible alias. Existing code referencing TaskStatus
    /// within the RevitCliBridge namespace will resolve to CliTaskStatus.
    /// </summary>
    // Do not add a using alias — just update all references directly.
}
