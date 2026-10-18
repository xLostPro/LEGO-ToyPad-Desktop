namespace LegoToyPad.Core.Protocol;

public static class Commands
{
    public const byte Wake = 0xB0;
    public const byte Color = 0xC0;
    public const byte Read = 0xD2;
    public const byte Write = 0xD3;
    public const byte Password = 0xE1;
}

public abstract record IncomingFrame;

/// <summary>A tag was placed on (Added) or lifted off a pad.</summary>
public sealed record TagEventFrame(int Pad, int Index, bool Added, string Uid) : IncomingFrame;

/// <summary>Reply to a command: <see cref="Data"/> is 16 bytes (4 pages) for reads, empty otherwise.</summary>
public sealed record ResponseFrame(byte CommandId, byte Status, byte Length, byte[] Data) : IncomingFrame
{
    public bool IsReadResponse => Data.Length > 0;

    // Short (length 2) acks only use the low nibble for errors, matching the CLI.
    public bool IsOk => IsReadResponse || (Length == 2 ? (Status & 0x0F) == 0 : Status == 0);
}

public sealed record UnknownFrame : IncomingFrame;

/// <summary>
/// 32-byte ToyPad USB frames. Outgoing: 0x55, length, command, id, payload, checksum.
/// Incoming: 0x55 replies (length, id, status, data) and 0x56 tag events.
/// </summary>
public static class PacketCodec
{
    public const int FrameSize = 32;
    private const byte TypeMessage = 0x55;
    private const byte TypeEvent = 0x56;

    public static byte[] Encode(byte command, byte commandId, ReadOnlySpan<byte> payload = default)
    {
        if (payload.Length > FrameSize - 5)
            throw new ArgumentException("Payload too large for a ToyPad frame.", nameof(payload));

        var frame = new byte[FrameSize];
        frame[0] = TypeMessage;
        frame[1] = (byte)(payload.Length + 2);
        frame[2] = command;
        frame[3] = commandId;
        payload.CopyTo(frame.AsSpan(4));

        byte checksum = 0;
        foreach (var b in frame) checksum += b;
        frame[payload.Length + 4] = checksum;
        return frame;
    }

    public static IncomingFrame Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < FrameSize) return new UnknownFrame();

        if (frame[0] == TypeEvent)
        {
            return new TagEventFrame(
                Pad: frame[2],
                Index: frame[4],
                Added: frame[5] == 0,
                Uid: Convert.ToHexString(frame.Slice(6, 7)));
        }

        if (frame[0] != TypeMessage) return new UnknownFrame();

        var length = frame[1];
        var commandId = frame[2];
        var status = frame[3];
        return length >= 18
            ? new ResponseFrame(commandId, status, length, frame.Slice(4, 16).ToArray())
            : new ResponseFrame(commandId, status, length, []);
    }
}
