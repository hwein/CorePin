namespace CorePin.Core.Platform;

/// Value null = no run key value present.
public sealed record RunKeyState(string? Value, bool Disabled);
