using CorePin.Core.Diagnostics;

namespace CorePin.Tests.Fakes;

/// Keeps every line so a test can assert on exact log wording without a file.
public sealed class RecordingLog : ILog
{
    private readonly Lock _gate = new();
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get { lock (_gate) return [.. _lines]; }
    }

    public bool IsEnabled(LogLevel level) => true;

    public void Write(LogLevel level, string category, string message)
    {
        lock (_gate) _lines.Add($"{level} {category} {message}");
    }

    public int Count(string fragment)
        => Lines.Count(l => l.Contains(fragment, StringComparison.Ordinal));

    public bool Has(string fragment) => Count(fragment) > 0;

    public bool Has(LogLevel level) => Lines.Any(l => l.StartsWith($"{level} ", StringComparison.Ordinal));

    /// Matches the level or any more severe one — for "no failure-class line" assertions.
    public bool HasAtOrAbove(LogLevel minimum)
        => Enum.GetValues<LogLevel>().Where(l => l >= minimum).Any(Has);

    /// Same as Has, but also pins the level a line was written at.
    public bool Has(LogLevel level, string fragment)
        => Lines.Any(l => l.StartsWith($"{level} ", StringComparison.Ordinal)
                          && l.Contains(fragment, StringComparison.Ordinal));

    public string Joined => string.Join("\n", Lines);
}
