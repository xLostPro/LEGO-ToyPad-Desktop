namespace LegoToyPad.Core;

public enum TokenKind
{
    Character,
    Vehicle
}

/// <summary>What a tag decodes to: a character (keyed by UID) or a vehicle/gadget.</summary>
public sealed record TagIdentity(TokenKind Kind, uint Id, string Name, string? World = null, int? Rebuild = null)
{
    public string Description => Kind == TokenKind.Character
        ? $"Character · {World} · ID {Id}"
        : $"Vehicle / Gadget · ID {Id}";
}

public enum IdentifyState
{
    Pending,
    Identified,
    Unrecognized
}

/// <summary>A tag currently sitting on the portal.</summary>
public sealed record ActiveTag(Pad Pad, int Index, string Uid, IdentifyState State = IdentifyState.Pending, TagIdentity? Identity = null)
{
    public string PadName => TagUtils.PadName(Pad);
}

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record LogEntry(LogLevel Level, string Message, DateTimeOffset Timestamp);

/// <summary>A 4-byte page as hex, e.g. ("0x24", "8539298E").</summary>
public sealed record PageValue(string Page, string Hex)
{
    public string AccessibleName => $"Page {Page}: {string.Join(" ", Hex.Chunk(2).Select(c => new string(c)))}";
}

public sealed record PasswordCheck(string Stored, string Expected, bool Matches);

public sealed record TagReadResult(
    Pad Pad,
    int Index,
    string Uid,
    IReadOnlyList<PageValue> Pages,
    uint RawTokenId,
    TagIdentity? Identity,
    PasswordCheck Password)
{
    public string PadName => TagUtils.PadName(Pad);
}

public sealed record PageDump(int Page, string? Hex, string? Error);

public sealed record WriteCheckResult(bool Ok, ActiveTag Tag, string? Reason);

public sealed record WriteResult(bool Ok, TokenKind Kind, uint Id, string Name, string Page24, string Page25);

public sealed record ResetPreview(ActiveTag Tag, IReadOnlyList<PageValue> Current, bool PasswordPageIsZero);

public enum ResetStatus
{
    Success,
    Partial,
    Failed
}

public sealed record ResetResult(ResetStatus Status, IReadOnlyList<string> ResetPages, IReadOnlyList<string> Failures);
