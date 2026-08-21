namespace CorePin.Core.Platform;

public sealed record TaskState(bool Present, string? ExePath, string? PrincipalSid, bool Enabled);
