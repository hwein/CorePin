namespace CorePin.Core.Diagnostics;

/// Only the test seam (CreateForTests) ever passes anything but Default.
internal readonly record struct FileLogLimits(
    long FileSizeBytes, int SessionFiles, long DirectoryBudgetBytes, int QueueCapacity)
{
    public static readonly FileLogLimits Default =
        new(10L * 1024 * 1024, 5, 50L * 1024 * 1024, 2000);
}
