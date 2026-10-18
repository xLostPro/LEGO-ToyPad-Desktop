using HidSharp;
using LegoToyPad.Core.Protocol;

namespace LegoToyPad.Core.Transport;

/// <summary>Cross-platform HID transport (Windows HID, Linux hidraw, macOS IOKit) via HidSharp.</summary>
public sealed class HidSharpTransportFactory : IHidTransportFactory
{
    public const int VendorId = 0x0E6F;
    public const int ProductId = 0x0241;

    public IHidTransport Open()
    {
        var device = DeviceList.Local.GetHidDevices(VendorId, ProductId).FirstOrDefault()
            ?? throw new ToyPadNotFoundException();

        HidStream stream;
        try
        {
            stream = device.Open();
        }
        catch (Exception ex)
        {
            var hint = OperatingSystem.IsLinux()
                ? " On Linux, install the udev rule from the README (or run once with sudo) so your user can open the device."
                : string.Empty;
            throw new IOException($"Found the ToyPad but could not open it: {ex.Message}.{hint}", ex);
        }

        return new HidSharpTransport(stream);
    }
}

public sealed class ToyPadNotFoundException()
    : Exception("ToyPad not found. Make sure it is plugged in via USB (PS3/PS4/Wii U pad; the Xbox pad is not supported).");

internal sealed class HidSharpTransport : IHidTransport
{
    private readonly HidStream _stream;
    private readonly int _outputLength;
    private readonly Thread _readThread;
    private readonly object _writeLock = new();
    private volatile bool _disposed;

    public event Action<byte[]>? FrameReceived;
    public event Action<Exception>? Faulted;

    public HidSharpTransport(HidStream stream)
    {
        _stream = stream;
        _stream.ReadTimeout = Timeout.Infinite;
        _outputLength = Math.Max(stream.Device.GetMaxOutputReportLength(), PacketCodec.FrameSize + 1);
        _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "ToyPad HID reader" };
        _readThread.Start();
    }

    public void Write(byte[] frame)
    {
        // The ToyPad has no numbered reports, so every OS expects report ID 0 as the first byte.
        var report = new byte[_outputLength];
        frame.AsSpan(0, Math.Min(frame.Length, PacketCodec.FrameSize)).CopyTo(report.AsSpan(1));
        lock (_writeLock)
        {
            _stream.Write(report);
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[Math.Max(_stream.Device.GetMaxInputReportLength(), PacketCodec.FrameSize + 1)];
        try
        {
            while (!_disposed)
            {
                var count = _stream.Read(buffer, 0, buffer.Length);
                if (count <= 0) continue;

                // HidSharp prefixes input reports with the report ID (0 here); strip it.
                var offset = count > PacketCodec.FrameSize ? count - PacketCodec.FrameSize : 0;
                var frame = new byte[PacketCodec.FrameSize];
                Array.Copy(buffer, offset, frame, 0, Math.Min(count - offset, PacketCodec.FrameSize));
                FrameReceived?.Invoke(frame);
            }
        }
        catch (Exception ex) when (!_disposed)
        {
            Faulted?.Invoke(ex);
        }
        catch
        {
            // Closing the stream unblocks Read with an exception; expected during Dispose.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Dispose();
    }
}
