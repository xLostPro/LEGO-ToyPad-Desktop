using System.Buffers.Binary;
using System.Collections.Concurrent;
using LegoToyPad.Core.Crypto;
using LegoToyPad.Core.Protocol;
namespace LegoToyPad.Core.Transport;

/// <summary>
/// In-memory ToyPad that answers read/write/password frames like the real hardware.
/// Used by the tests and by the app's <c>--demo</c> mode.
/// </summary>
public sealed class SimulatedToyPad : IHidTransport, IHidTransportFactory
{
    private const int PageCount = 45;
    private readonly ConcurrentDictionary<int, byte[]> _tags = new();

    public event Action<byte[]>? FrameReceived;
    public event Action<Exception>? Faulted;

    /// <summary>Slot indices that answer writes with status 0xF2 (like a center slot that can't write).</summary>
    public HashSet<int> RefuseWritesOn { get; } = [];

    /// <summary>Set if a command arrived while a previous one was still unanswered.</summary>
    public bool OverlapDetected { get; private set; }

    private int _inFlight;

    public IHidTransport Open() => this;

    /// <summary>A portal with a blank tag on center, a character on the left and a vehicle on the right.</summary>
    public static SimulatedToyPad CreateDemo()
    {
        var pad = new SimulatedToyPad();
        pad.PlaceTag(pad: 1, index: 0, "04A1B2C3D4E580");
        pad.PlaceTag(pad: 2, index: 1, "0456789ABCDEF0", initialize: m => WriteCharacter(m, "0456789ABCDEF0", 1));
        pad.PlaceTag(pad: 3, index: 4, "04123456789ABC", initialize: m => BinaryPrimitives.WriteUInt32LittleEndian(m.AsSpan(0x24 * 4), 1000));
        return pad;
    }

    /// <summary>Fills tag memory the way a genuine character tag is programmed (data + UID password).</summary>
    public static void WriteCharacter(byte[] memory, string uid, uint characterId)
    {
        TagCrypto.EncryptCharacter(uid, characterId).CopyTo(memory, 0x24 * 4);
        BinaryPrimitives.WriteUInt32LittleEndian(memory.AsSpan(0x2B * 4), TagCrypto.GeneratePassword(uid));
    }

    public void PlaceTag(int pad, int index, string uid, string? reportedUid = null, Action<byte[]>? initialize = null)
    {
        var memory = new byte[PageCount * 4];
        var u = Convert.FromHexString(uid);
        memory[0] = u[0];
        memory[1] = u[1];
        memory[2] = u[2];
        memory[3] = (byte)(u[0] ^ u[1] ^ u[2] ^ 0x88);
        Array.Copy(u, 3, memory, 4, 4);
        memory[8] = (byte)(u[3] ^ u[4] ^ u[5] ^ u[6]);
        initialize?.Invoke(memory);
        _tags[index] = memory;
        SendEvent(pad, index, added: true, reportedUid ?? uid);
    }

    public void RemoveTag(int pad, int index, bool fireEvent = true)
    {
        _tags.TryRemove(index, out _);
        if (fireEvent) SendEvent(pad, index, added: false, "00000000000000");
    }

    public byte[] Page(int index, int page) => _tags[index].AsSpan(page * 4, 4).ToArray();

    public void Unplug() => Faulted?.Invoke(new IOException("device removed"));

    public void Write(byte[] frame)
    {
        var command = frame[2];
        var id = frame[3];
        var payload = frame.AsSpan(4, frame[1] - 2).ToArray();
        byte[]? reply = command switch
        {
            Commands.Read => HandleRead(id, payload),
            Commands.Write => HandleWrite(id, payload),
            Commands.Password => Short(id, 0x00),
            _ => null
        };
        if (reply is null) return;

        if (Interlocked.Increment(ref _inFlight) > 1) OverlapDetected = true;
        Task.Run(async () =>
        {
            await Task.Delay(2);
            Interlocked.Decrement(ref _inFlight);
            FrameReceived?.Invoke(reply);
        });
    }

    private byte[] HandleRead(byte id, byte[] payload)
    {
        if (!_tags.TryGetValue(payload[0], out var memory)) return Short(id, 0xF0);

        var frame = new byte[32];
        frame[0] = 0x55;
        frame[1] = 0x12;
        frame[2] = id;
        for (var i = 0; i < 16; i++) frame[4 + i] = memory[(payload[1] * 4 + i) % memory.Length];
        return frame;
    }

    private byte[] HandleWrite(byte id, byte[] payload)
    {
        if (RefuseWritesOn.Contains(payload[0])) return Short(id, 0xF2);

        // Like the real pad, a write to any slot goes to the tag on that pad; index 1 is the center write slot.
        var memory = _tags.TryGetValue(payload[0], out var m) ? m : payload[0] == 1 && _tags.TryGetValue(0, out var c) ? c : null;
        if (memory is null) return Short(id, 0xF0);

        Array.Copy(payload, 2, memory, payload[1] * 4, 4);
        return Short(id, 0x00);
    }

    private static byte[] Short(byte id, byte status)
    {
        var frame = new byte[32];
        frame[0] = 0x55;
        frame[1] = 0x02;
        frame[2] = id;
        frame[3] = status;
        return frame;
    }

    private void SendEvent(int pad, int index, bool added, string uid)
    {
        var frame = new byte[32];
        frame[0] = 0x56;
        frame[2] = (byte)pad;
        frame[4] = (byte)index;
        frame[5] = added ? (byte)0 : (byte)1;
        Convert.FromHexString(uid).CopyTo(frame, 6);
        FrameReceived?.Invoke(frame);
    }

    public void Dispose()
    {
    }
}
