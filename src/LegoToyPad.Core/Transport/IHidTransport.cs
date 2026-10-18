namespace LegoToyPad.Core.Transport;

/// <summary>Raw 32-byte frame pipe to a ToyPad. Abstracted so the protocol can be tested without hardware.</summary>
public interface IHidTransport : IDisposable
{
    /// <summary>Raised on a background thread for every 32-byte frame received.</summary>
    event Action<byte[]>? FrameReceived;

    /// <summary>Raised once when the device goes away (unplugged, read error).</summary>
    event Action<Exception>? Faulted;

    void Write(byte[] frame);
}

public interface IHidTransportFactory
{
    IHidTransport Open();
}
