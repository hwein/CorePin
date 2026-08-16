using CorePin.Core.Diagnostics;

namespace CorePin.Tests.Fakes;

/// Keeps every line so a test can assert on the wording of S02 §6 without a file.
public sealed class RecordingLog : ILog
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines => _lines;

    public bool IsEnabled(LogLevel level) => true;

    public void Write(LogLevel level, string category, string message)
        => _lines.Add($"{level} {category} {message}");

    public int Count(string fragment)
        => _lines.Count(l => l.Contains(fragment, StringComparison.Ordinal));

    public bool Has(string fragment) => Count(fragment) > 0;

    /// Same as Has, but also pins the level a line was written at.
    public bool Has(LogLevel level, string fragment)
        => _lines.Any(l => l.StartsWith($"{level} ", StringComparison.Ordinal)
                           && l.Contains(fragment, StringComparison.Ordinal));

    public string Joined => string.Join("\n", _lines);
}
