namespace CorePin.Core.Topology;

internal enum NoteStep { L3Groups, CoreAssignment, EmptyGroups, Smt }

/// Ordered so that two runs produce the same list.
internal sealed class NoteCollector
{
    private readonly List<(NoteStep Step, ulong Mask, string Text)> _notes = [];

    public bool HasStructureNote { get; private set; }

    public void Add(NoteStep step, ulong mask, string text, bool structural = false)
    {
        _notes.Add((step, mask, text));
        if (structural) HasStructureNote = true;
    }

    public IReadOnlyList<string> ToList()
        => [.. _notes.OrderBy(n => n.Step).ThenBy(n => n.Mask).ThenBy(n => n.Text, StringComparer.Ordinal)
                     .Select(n => n.Text)];
}
