using LegoToyPad.Core.Protocol;
using LegoToyPad.Core.Transport;

namespace LegoToyPad.Core;

/// <summary>A command the ToyPad answered with an error status (e.g. 0xF2 when a write is refused).</summary>
public sealed class ToyPadCommandException(byte status, string message) : Exception(message)
{
    public byte Status { get; } = status;
}

public enum PasswordMode : byte
{
    Disabled = 0,
    UidDerived = 1,
    Custom = 2
}

/// <summary>
/// Low-level ToyPad protocol over an <see cref="IHidTransport"/>. Commands are serialized
/// (one in flight at a time) so background scans, auto-identify and user actions can't
/// interleave on the wire.
/// </summary>
public sealed class ToyPadDevice : IDisposable
{
    public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(5);

    private readonly IHidTransport _transport;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _pendingLock = new();
    private readonly TimeSpan _commandTimeout;
    private (byte Id, TaskCompletionSource<ResponseFrame> Tcs)? _pending;
    private byte _nextCommandId = 1;
    private Timer? _keepAlive;
    private int _keepAlivePauses;
    private int _faulted;

    public event Action<TagEventFrame>? TagEvent;
    public event Action<Exception>? Disconnected;

    public ToyPadDevice(IHidTransport transport, TimeSpan? commandTimeout = null)
    {
        _transport = transport;
        _commandTimeout = commandTimeout ?? DefaultCommandTimeout;
        _transport.FrameReceived += OnFrame;
        _transport.Faulted += OnFaulted;
    }

    /// <summary>Sends the wake handshake ("(c) LEGO 2014"), retrying a couple of times.</summary>
    public async Task WakeAsync(CancellationToken ct = default)
    {
        var frame = PacketCodec.Encode(Commands.Wake, 0x01, "(c) LEGO 2014"u8);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _transport.Write(frame);
                return;
            }
            catch when (attempt < 3)
            {
                await Task.Delay(200, ct);
            }
        }
    }

    /// <summary>Reads 16 bytes (four 4-byte pages) starting at <paramref name="page"/>.</summary>
    public async Task<byte[]> ReadPagesAsync(int tagIndex, int page, CancellationToken ct = default)
    {
        var response = await SendAsync(Commands.Read, [(byte)tagIndex, (byte)page], ct);
        if (!response.IsReadResponse)
            throw new ToyPadCommandException(response.Status, $"Read response had no data (status: 0x{response.Status:X2})");
        return response.Data;
    }

    /// <summary>Reads a single 4-byte page.</summary>
    public async Task<byte[]> ReadPageAsync(int tagIndex, int page, CancellationToken ct = default) =>
        (await ReadPagesAsync(tagIndex, page, ct))[..4];

    public async Task WritePageAsync(int tagIndex, int page, ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (data.Length != 4) throw new ArgumentException("Data must be exactly 4 bytes.", nameof(data));

        var payload = new byte[6];
        payload[0] = (byte)tagIndex;
        payload[1] = (byte)page;
        data.Span.CopyTo(payload.AsSpan(2));

        var response = await SendAsync(Commands.Write, payload, ct);
        if (!response.IsOk)
            throw new ToyPadCommandException(response.Status, $"Write failed with status: 0x{response.Status:X2}");
    }

    public async Task SetPasswordModeAsync(int tagIndex, PasswordMode mode, ReadOnlyMemory<byte> customPassword = default, CancellationToken ct = default)
    {
        var payload = new byte[6];
        payload[0] = (byte)tagIndex;
        payload[1] = (byte)mode;
        if (mode == PasswordMode.Custom)
        {
            if (customPassword.Length != 4) throw new ArgumentException("Custom password mode requires a 4-byte password.", nameof(customPassword));
            customPassword.Span.CopyTo(payload.AsSpan(2));
        }

        var response = await SendAsync(Commands.Password, payload, ct);
        if (!response.IsOk)
            throw new ToyPadCommandException(response.Status, $"Command failed with status: 0x{response.Status:X2}");
    }

    /// <summary>
    /// Periodically sends an all-pads "LEDs off" frame so Windows doesn't let the HID
    /// connection idle out. This app never lights the LEDs, so it has no visible effect.
    /// </summary>
    public void EnableKeepAlive(TimeSpan interval)
    {
        _keepAlive?.Dispose();
        _keepAlive = new Timer(_ => SendKeepAlive(), null, interval, interval);
    }

    /// <summary>Suspends keep-alive frames until the returned scope is disposed (used around writes).</summary>
    public IDisposable PauseKeepAlive()
    {
        Interlocked.Increment(ref _keepAlivePauses);
        return new Scope(() => Interlocked.Decrement(ref _keepAlivePauses));
    }

    private void SendKeepAlive()
    {
        if (Volatile.Read(ref _keepAlivePauses) > 0 || !_gate.Wait(0)) return;
        try
        {
            _transport.Write(PacketCodec.Encode(Commands.Color, AllocateCommandId(), [0, 0, 0, 0]));
        }
        catch
        {
            // A real failure surfaces through Faulted or the next command.
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ResponseFrame> SendAsync(byte command, byte[] payload, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (Volatile.Read(ref _faulted) != 0) throw new IOException("ToyPad is disconnected.");

            var id = AllocateCommandId();
            var tcs = new TaskCompletionSource<ResponseFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pendingLock) _pending = (id, tcs);

            try
            {
                _transport.Write(PacketCodec.Encode(command, id, payload));
                return await tcs.Task.WaitAsync(_commandTimeout, ct);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"ToyPad did not answer command 0x{command:X2} within {_commandTimeout.TotalSeconds:0}s.");
            }
            finally
            {
                lock (_pendingLock) _pending = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private byte AllocateCommandId()
    {
        var id = _nextCommandId;
        _nextCommandId = (byte)(id % 255 + 1);
        return id;
    }

    private void OnFrame(byte[] frame)
    {
        switch (PacketCodec.Decode(frame))
        {
            case TagEventFrame tagEvent:
                TagEvent?.Invoke(tagEvent);
                break;

            case ResponseFrame response:
                TaskCompletionSource<ResponseFrame>? tcs = null;
                lock (_pendingLock)
                {
                    if (_pending is { } p && p.Id == response.CommandId) tcs = p.Tcs;
                }
                tcs?.TrySetResult(response);
                break;
        }
    }

    private void OnFaulted(Exception ex)
    {
        if (Interlocked.Exchange(ref _faulted, 1) != 0) return;
        lock (_pendingLock) _pending?.Tcs.TrySetException(new IOException("ToyPad was disconnected.", ex));
        Disconnected?.Invoke(ex);
    }

    public void Dispose()
    {
        _keepAlive?.Dispose();
        _transport.FrameReceived -= OnFrame;
        _transport.Faulted -= OnFaulted;
        _transport.Dispose();
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;
        public void Dispose() => Interlocked.Exchange(ref _onDispose, null)?.Invoke();
    }
}
