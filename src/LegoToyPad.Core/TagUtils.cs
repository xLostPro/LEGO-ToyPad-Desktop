namespace LegoToyPad.Core;

public enum Pad
{
    Unknown = 0,
    Center = 1,
    Left = 2,
    Right = 3
}

public static class TagUtils
{
    public static string PadName(Pad pad) => pad switch
    {
        Pad.Center => "CENTER",
        Pad.Left => "LEFT",
        Pad.Right => "RIGHT",
        _ => $"PAD-{(int)pad}"
    };

    /// <summary>Slot 0 is the center pad, 1-3 the left pad and 4-6 the right pad.</summary>
    public static Pad PadFromIndex(int index) => index switch
    {
        0 => Pad.Center,
        >= 1 and <= 3 => Pad.Left,
        >= 4 and <= 6 => Pad.Right,
        _ => Pad.Unknown
    };

    /// <summary>
    /// Pulls the 7-byte NTAG UID out of pages 0 and 1, validating the BCC0 check byte.
    /// Returns null for empty/garbage reads.
    /// </summary>
    public static string? TryExtractUid(ReadOnlySpan<byte> page0, ReadOnlySpan<byte> page1)
    {
        if (page0.Length < 4 || page1.Length < 4) return null;

        var expectedBcc0 = (byte)(page0[0] ^ page0[1] ^ page0[2] ^ 0x88);
        if (page0[3] != expectedBcc0) return null;

        Span<byte> uid = stackalloc byte[7];
        page0[..3].CopyTo(uid);
        page1[..4].CopyTo(uid[3..]);
        if (!uid.ContainsAnyExcept((byte)0)) return null;

        return Convert.ToHexString(uid);
    }
}
