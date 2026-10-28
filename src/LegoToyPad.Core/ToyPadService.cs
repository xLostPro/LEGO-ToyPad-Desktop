using System.Buffers.Binary;
using LegoToyPad.Core.Crypto;
using LegoToyPad.Core.Data;
using LegoToyPad.Core.Protocol;
using LegoToyPad.Core.Transport;

namespace LegoToyPad.Core;

/// <summary>
/// Tag tracking plus the read / write / reset workflows from the original CLI, returning
/// structured results and emitting log lines instead of printing to a console.
/// Events are raised on background threads.
/// </summary>
public sealed class ToyPadService : IDisposable
{
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

    private const int CenterWriteSlot = 1;
    private const byte StatusWriteRefused = 0xF2;
    private const int PageUid = 0x00;
    private const int PageData = 0x23;
    private const int PageToken = 0x24;
    private const int PageToken2 = 0x25;
    private const int PageGameplay3 = 0x26;
    private const int PagePassword = 0x2B;
    private static readonly int[] KnownSlots = [0, 1, 2, 3, 4, 5, 6];
    private static readonly int[] GameplayPages = [PageToken, PageToken2, PageGameplay3];
    private static readonly byte[] Zero4 = [0, 0, 0, 0];

    private readonly IHidTransportFactory _transportFactory;
    private readonly TimeSpan _startupDelay;
    private readonly object _tagsLock = new();
    private readonly Dictionary<(Pad, int), ActiveTag> _tags = [];
    private ToyPadDevice? _device;
    private int _rescanning;

    public event Action<LogEntry>? Log;
    public event Action<IReadOnlyList<ActiveTag>>? TagsChanged;
    public event Action<bool, string?>? ConnectionChanged;

    public ToyPadService(IHidTransportFactory transportFactory, TimeSpan? startupDelay = null)
    {
        _transportFactory = transportFactory;
        _startupDelay = startupDelay ?? TimeSpan.FromMilliseconds(1200);
    }

    public bool IsConnected => _device is not null;

    public IReadOnlyList<ActiveTag> ActiveTags
    {
        get
        {
            lock (_tagsLock)
                return _tags.Values.OrderBy(t => t.Pad).ThenBy(t => t.Index).ToList();
        }
    }

    private ToyPadDevice Device => _device ?? throw new InvalidOperationException("ToyPad is not connected.");

    // ------------------------------------------------------------------
    // Connection
    // ------------------------------------------------------------------

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_device is not null) return;

        Info("Connecting to ToyPad...");
        ToyPadDevice device;
        try
        {
            device = new ToyPadDevice(_transportFactory.Open());
        }
        catch (Exception ex)
        {
            ConnectionChanged?.Invoke(false, ex.Message);
            throw;
        }

        device.TagEvent += OnTagEvent;
        device.Disconnected += OnDeviceLost;
        _device = device;

        try
        {
            await Task.Delay(300, ct);
            await device.WakeAsync(ct);
        }
        catch
        {
            Disconnect();
            throw;
        }

        Emit(LogLevel.Success, "Connected!");
        ConnectionChanged?.Invoke(true, null);
        device.EnableKeepAlive(KeepAliveInterval);

        await Task.Delay(_startupDelay, ct);
        await RescanAsync("startup", ct);
    }

    public void Disconnect() => TearDown(null);

    private void OnDeviceLost(Exception ex)
    {
        Emit(LogLevel.Error, $"ToyPad connection lost: {ex.Message}");
        TearDown(ex.Message);
    }

    private void TearDown(string? error)
    {
        var device = Interlocked.Exchange(ref _device, null);
        if (device is null) return;

        device.TagEvent -= OnTagEvent;
        device.Disconnected -= OnDeviceLost;
        try
        {
            device.Dispose();
        }
        catch
        {
            // Already gone.
        }

        lock (_tagsLock) _tags.Clear();
        ConnectionChanged?.Invoke(false, error);
        RaiseTagsChanged();
        Info("Disconnected.");
    }

    public void Dispose() => Disconnect();

    // ------------------------------------------------------------------
    // Tag tracking
    // ------------------------------------------------------------------

    private void OnTagEvent(TagEventFrame e)
    {
        var pad = (Pad)e.Pad;
        if (e.Added)
        {
            var tag = new ActiveTag(pad, e.Index, e.Uid);
            lock (_tagsLock) _tags[(pad, e.Index)] = tag;
            Info($"Tag detected on {TagUtils.PadName(pad)} pad (index {e.Index}) UID={e.Uid}");
            RaiseTagsChanged();
            _ = IdentifyAndUpdateAsync(tag);
        }
        else
        {
            lock (_tagsLock) _tags.Remove((pad, e.Index));
            Info($"Tag removed from {TagUtils.PadName(pad)} pad (index {e.Index})");
            RaiseTagsChanged();
        }
    }

    public async Task RescanAsync(string reason = "manual", CancellationToken ct = default)
    {
        if (_device is null || Interlocked.Exchange(ref _rescanning, 1) == 1) return;

        try
        {
            Info($"Running full slot scan ({reason})...");
            var discovered = new List<ActiveTag>();
            foreach (var index in KnownSlots)
            {
                try
                {
                    var pages = await Device.ReadPagesAsync(index, PageUid, ct);
                    var uid = TagUtils.TryExtractUid(pages.AsSpan(0, 4), pages.AsSpan(4, 4));
                    if (uid is not null) discovered.Add(new ActiveTag(TagUtils.PadFromIndex(index), index, uid));
                }
                catch (Exception ex) when (ex is ToyPadCommandException or TimeoutException)
                {
                    // Empty or unreadable slot.
                }
            }

            List<ActiveTag> toIdentify;
            lock (_tagsLock)
            {
                _tags.Clear();
                foreach (var tag in discovered) _tags[(tag.Pad, tag.Index)] = tag;
                toIdentify = _tags.Values.ToList();
            }

            Emit(LogLevel.Success, $"Scan complete. {toIdentify.Count} tag(s) active.");
            RaiseTagsChanged();

            foreach (var tag in toIdentify) await IdentifyAndUpdateAsync(tag, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Emit(LogLevel.Error, $"Slot scan failed: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _rescanning, 0);
        }
    }

    /// <summary>Quietly identifies a freshly detected tag so the pad list can show its name.</summary>
    private async Task IdentifyAndUpdateAsync(ActiveTag tag, CancellationToken ct = default)
    {
        TagIdentity? identity = null;
        string? uid = null;
        try
        {
            uid = await ResolveTrustedUidAsync(tag, ct);
            await TryAuthenticateAsync(tag.Index, ct);
            identity = await IdentifyAsync(tag.Index, uid, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Emit(LogLevel.Warning, $"Could not auto-identify {tag.PadName} tag: {ex.Message}");
        }

        UpdateTag(tag, current => current with
        {
            Uid = uid ?? current.Uid,
            State = identity is null ? IdentifyState.Unrecognized : IdentifyState.Identified,
            Identity = identity
        });
    }

    private void UpdateTag(ActiveTag tag, Func<ActiveTag, ActiveTag> update)
    {
        lock (_tagsLock)
        {
            // Skip if the tag was lifted off while we were talking to it.
            if (!_tags.TryGetValue((tag.Pad, tag.Index), out var current)) return;
            _tags[(tag.Pad, tag.Index)] = update(current);
        }
        RaiseTagsChanged();
    }

    private ActiveTag? CenterTag
    {
        get
        {
            lock (_tagsLock) return _tags.Values.FirstOrDefault(t => t.Pad == Pad.Center);
        }
    }

    private ActiveTag GetTag(Pad pad, int index)
    {
        lock (_tagsLock)
        {
            return _tags.TryGetValue((pad, index), out var tag)
                ? tag
                : throw new InvalidOperationException("That tag is no longer on the portal. Rescan and try again.");
        }
    }

    // ------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// The UID in the live "tag detected" event isn't always reliable, and the character
    /// cipher is keyed off it. Re-read pages 0/1 and validate the BCC before trusting it.
    /// </summary>
    private async Task<string> ResolveTrustedUidAsync(ActiveTag tag, CancellationToken ct)
    {
        int[] candidates = tag.Pad == Pad.Center && tag.Index != CenterWriteSlot ? [tag.Index, CenterWriteSlot] : [tag.Index];
        foreach (var index in candidates)
        {
            try
            {
                var pages = await Device.ReadPagesAsync(index, PageUid, ct);
                var uid = TagUtils.TryExtractUid(pages.AsSpan(0, 4), pages.AsSpan(4, 4));
                if (uid is null) continue;

                if (uid != tag.Uid)
                    Emit(LogLevel.Warning, $"Auto-detected UID ({tag.Uid}) didn't match the tag's own memory ({uid}) — using the tag memory UID.");
                return uid;
            }
            catch (Exception ex) when (ex is ToyPadCommandException or TimeoutException)
            {
                // Try the next candidate.
            }
        }

        Emit(LogLevel.Warning, $"Could not verify UID from tag memory for {tag.PadName} [index {tag.Index}]; falling back to auto-detected UID {tag.Uid}.");
        return tag.Uid;
    }

    /// <summary>
    /// Always attempt UID-password auth first: a protected tag reads its protected pages as
    /// zeros until unlocked, and on an unprotected tag this is a harmless no-op.
    /// </summary>
    private async Task TryAuthenticateAsync(int index, CancellationToken ct, bool logFailure = false)
    {
        try
        {
            await Device.SetPasswordModeAsync(index, PasswordMode.UidDerived, ct: ct);
        }
        catch (Exception ex) when (ex is ToyPadCommandException or TimeoutException)
        {
            if (logFailure) Emit(LogLevel.Warning, $"Could not set default password mode: {ex.Message}");
        }
    }

    private async Task<TagIdentity?> IdentifyAsync(int index, string uid, CancellationToken ct)
    {
        var pages = await Device.ReadPagesAsync(index, PageToken, ct);
        return Decode(pages.AsSpan(0, 8), uid);
    }

    /// <summary>Matches pages 0x24/0x25 against the vehicle list, then against every character encrypted for this UID.</summary>
    internal static TagIdentity? Decode(ReadOnlySpan<byte> tokenPages, string uid)
    {
        var rawTokenId = BinaryPrimitives.ReadUInt32LittleEndian(tokenPages);
        if (TokenDatabase.FindVehicle(rawTokenId) is { } vehicle)
            return new TagIdentity(TokenKind.Vehicle, vehicle.Id, vehicle.Name, Rebuild: vehicle.Rebuild);

        if (uid.Length == 14)
        {
            var characterId = TagCrypto.DecryptCharacter(uid, tokenPages[..8]);
            if (TokenDatabase.FindCharacter(characterId) is { } character
                && tokenPages[..8].SequenceEqual(TagCrypto.EncryptCharacter(uid, character.Id)))
            {
                return new TagIdentity(TokenKind.Character, character.Id, character.Name, character.World);
            }
        }

        return null;
    }

    private async Task WritePageWithCenterFallbackAsync(ActiveTag tag, int page, byte[] data, CancellationToken ct)
    {
        try
        {
            await Device.WritePageAsync(tag.Index, page, data, ct);
        }
        catch (ToyPadCommandException ex) when (ex.Status == StatusWriteRefused && tag.Pad == Pad.Center && tag.Index != CenterWriteSlot)
        {
            Emit(LogLevel.Warning, $"Write returned 0xF2 on index {tag.Index}, retrying page 0x{page:X2} with CENTER slot {CenterWriteSlot}...");
            await Device.WritePageAsync(CenterWriteSlot, page, data, ct);
        }
    }

    private async Task ConfigurePasswordModeAsync(int index, byte[] passwordPage, CancellationToken ct)
    {
        try
        {
            if (passwordPage.AsSpan().SequenceEqual(Zero4))
            {
                await Device.SetPasswordModeAsync(index, PasswordMode.Disabled, ct: ct);
                Emit(LogLevel.Success, "ToyPad auth mode set to disable password (blank-tag bootstrap).");
            }
            else
            {
                await Device.SetPasswordModeAsync(index, PasswordMode.UidDerived, ct: ct);
                Emit(LogLevel.Success, "ToyPad auth mode set to default UID password mode.");
            }
        }
        catch (Exception ex) when (ex is ToyPadCommandException or TimeoutException)
        {
            Emit(LogLevel.Warning, $"Could not configure ToyPad password mode: {ex.Message}");
        }
    }

    private ActiveTag RequireCenterTag(string hint) =>
        CenterTag ?? throw new InvalidOperationException($"No tag detected on the CENTER pad. {hint}");

    // ------------------------------------------------------------------
    // Read
    // ------------------------------------------------------------------

    public async Task<TagReadResult> ReadTagAsync(Pad pad, int index, CancellationToken ct = default)
    {
        var tag = GetTag(pad, index);
        Info($"Reading tag data for {tag.PadName} [index {tag.Index}]...");

        var uid = await ResolveTrustedUidAsync(tag, ct);
        await TryAuthenticateAsync(tag.Index, ct, logFailure: true);

        var data = await Device.ReadPagesAsync(tag.Index, PageData, ct); // 0x23..0x26
        var passwordPage = await Device.ReadPageAsync(tag.Index, PagePassword, ct);

        var tokenPages = data.AsSpan(4, 8);
        var identity = Decode(tokenPages, uid);
        var storedPassword = BinaryPrimitives.ReadUInt32LittleEndian(passwordPage);
        var expectedPassword = TagCrypto.GeneratePassword(uid);

        if (identity is not null)
            Emit(LogLevel.Success, $"Identified {identity.Kind.ToString().ToLowerInvariant()}: {identity.Name} (ID {identity.Id})");
        else
            Info("Tag data not found in known character/vehicle databases (blank, unknown, or custom).");

        UpdateTag(tag, current => current with
        {
            Uid = uid,
            State = identity is null ? IdentifyState.Unrecognized : IdentifyState.Identified,
            Identity = identity
        });

        return new TagReadResult(
            tag.Pad,
            tag.Index,
            uid,
            [
                new("0x23", Convert.ToHexString(data, 0, 4)),
                new("0x24", Convert.ToHexString(data, 4, 4)),
                new("0x25", Convert.ToHexString(data, 8, 4)),
                new("0x26", Convert.ToHexString(data, 12, 4)),
                new("0x2B", Convert.ToHexString(passwordPage))
            ],
            BinaryPrimitives.ReadUInt32LittleEndian(tokenPages),
            identity,
            new PasswordCheck($"{storedPassword:X8}", $"{expectedPassword:X8}", storedPassword == expectedPassword));
    }

    /// <summary>Diagnostic raw dump of pages 0x00-0x2C, four pages per USB read.</summary>
    public async Task<IReadOnlyList<PageDump>> DumpPagesAsync(Pad pad, int index, CancellationToken ct = default)
    {
        const int lastPage = 0x2C;
        var tag = GetTag(pad, index);
        Info($"Dumping raw pages 0x00-0x{lastPage:X2} for {tag.PadName} UID={tag.Uid}...");
        await TryAuthenticateAsync(tag.Index, ct, logFailure: true);

        var result = new List<PageDump>();
        for (var start = 0; start <= lastPage; start += 4)
        {
            byte[]? block = null;
            string? error = null;
            try
            {
                block = await Device.ReadPagesAsync(tag.Index, start, ct);
            }
            catch (Exception ex) when (ex is ToyPadCommandException or TimeoutException)
            {
                error = ex.Message;
            }

            for (var page = start; page < start + 4 && page <= lastPage; page++)
            {
                var hex = block is null ? null : Convert.ToHexString(block, (page - start) * 4, 4);
                result.Add(new PageDump(page, hex, error));
                if (hex is not null) Info($"  0x{page:X2}: {hex}");
                else Emit(LogLevel.Warning, $"  0x{page:X2}: read failed ({error})");
            }
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Write (CENTER only — the side pads have no write antenna)
    // ------------------------------------------------------------------

    /// <summary>Checks the CENTER tag has no password and accepts a probe write.</summary>
    public async Task<WriteCheckResult> PrepareWriteAsync(CancellationToken ct = default)
    {
        var tag = RequireCenterTag("Place a blank custom NFC tag on center.");
        using var keepAlivePause = Device.PauseKeepAlive();

        Info($"Preparing write for CENTER [index {tag.Index}] UID={tag.Uid}");
        var passwordPage = await Device.ReadPageAsync(tag.Index, PagePassword, ct);
        var probeData = await Device.ReadPageAsync(tag.Index, PageGameplay3, ct);

        if (!passwordPage.AsSpan().SequenceEqual(Zero4))
            return new WriteCheckResult(false, tag, $"Tag has a non-zero password page (0x2B = {Convert.ToHexString(passwordPage)}). Use a blank custom NFC tag.");
        Emit(LogLevel.Success, "Tag password check passed (no password).");

        await ConfigurePasswordModeAsync(tag.Index, passwordPage, ct);

        await WritePageWithCenterFallbackAsync(tag, PageGameplay3, probeData, ct);
        var verify = await Device.ReadPageAsync(tag.Index, PageGameplay3, ct);
        if (!verify.AsSpan().SequenceEqual(probeData))
            return new WriteCheckResult(false, tag, "Writable check failed: page verify mismatch after probe write.");

        Emit(LogLevel.Success, "Writable check passed. Tag is ready to write.");
        return new WriteCheckResult(true, tag, null);
    }

    /// <summary>
    /// Writes a character or vehicle to the CENTER tag. <paramref name="expectedUid"/> (from
    /// <see cref="PrepareWriteAsync"/>) guards against the tag being swapped in between.
    /// </summary>
    public async Task<WriteResult> CommitWriteAsync(TokenKind kind, uint id, string? expectedUid = null, CancellationToken ct = default)
    {
        var tag = RequireCenterTag("Place the tag back on center.");
        using var keepAlivePause = Device.PauseKeepAlive();

        // Character data is encrypted with the UID, so it must be the tag's real one.
        var uid = await ResolveTrustedUidAsync(tag, ct);
        if (expectedUid is not null && !string.Equals(uid, expectedUid, StringComparison.OrdinalIgnoreCase) && !string.Equals(tag.Uid, expectedUid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The tag on CENTER changed since it was checked. Run \"Check tag\" again.");

        string name;
        byte[] page24;
        byte[] page25;
        if (kind == TokenKind.Character)
        {
            var character = TokenDatabase.FindCharacter(id) ?? throw new ArgumentException($"Character ID {id} not found.");
            name = character.Name;
            var encrypted = TagCrypto.EncryptCharacter(uid, id);
            page24 = encrypted[..4];
            page25 = encrypted[4..];
        }
        else
        {
            var vehicle = TokenDatabase.FindVehicle(id) ?? throw new ArgumentException($"Vehicle/Gadget ID {id} not found.");
            name = vehicle.Name;
            page24 = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(page24, id);
            page25 = [.. Zero4];
        }

        Info($"Writing {kind.ToString().ToLowerInvariant()} \"{name}\" (ID {id}) to CENTER tag...");
        await WritePageWithCenterFallbackAsync(tag, PageToken, page24, ct);
        await WritePageWithCenterFallbackAsync(tag, PageToken2, page25, ct);

        var verify = await Device.ReadPagesAsync(tag.Index, PageToken, ct);
        var ok = verify.AsSpan(0, 4).SequenceEqual(page24) && verify.AsSpan(4, 4).SequenceEqual(page25);
        if (ok)
        {
            Emit(LogLevel.Success, "Write successful. Verified on pages 0x24 and 0x25.");
            _ = IdentifyAndUpdateAsync(tag with { Uid = uid });
        }
        else
        {
            Emit(LogLevel.Error, "Write failed: verification mismatch.");
        }

        return new WriteResult(ok, kind, id, name, Convert.ToHexString(page24), Convert.ToHexString(page25));
    }

    // ------------------------------------------------------------------
    // Reset (CENTER only)
    // ------------------------------------------------------------------

    public async Task<ResetPreview> PrepareResetAsync(CancellationToken ct = default)
    {
        var tag = RequireCenterTag("Place the custom NFC tag on center.");
        using var keepAlivePause = Device.PauseKeepAlive();

        var data = await Device.ReadPagesAsync(tag.Index, PageToken, ct); // 0x24..0x27
        var passwordPage = await Device.ReadPageAsync(tag.Index, PagePassword, ct);
        await ConfigurePasswordModeAsync(tag.Index, passwordPage, ct);
        Info("Read current tag values for reset preview.");

        return new ResetPreview(
            tag,
            [
                new("0x24", Convert.ToHexString(data, 0, 4)),
                new("0x25", Convert.ToHexString(data, 4, 4)),
                new("0x26", Convert.ToHexString(data, 8, 4)),
                new("0x2B", Convert.ToHexString(passwordPage))
            ],
            passwordPage.AsSpan().SequenceEqual(Zero4));
    }

    public async Task<ResetResult> CommitResetAsync(bool clearPasswordPage, string? expectedUid = null, CancellationToken ct = default)
    {
        var tag = RequireCenterTag("Place the tag back on center.");
        using var keepAlivePause = Device.PauseKeepAlive();

        var uid = await ResolveTrustedUidAsync(tag, ct);
        if (expectedUid is not null && !string.Equals(uid, expectedUid, StringComparison.OrdinalIgnoreCase) && !string.Equals(tag.Uid, expectedUid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The tag on CENTER changed since it was previewed. Run \"Preview reset\" again.");

        var passwordPage = await Device.ReadPageAsync(tag.Index, PagePassword, ct);
        var pages = GameplayPages.ToList();
        if (clearPasswordPage && !passwordPage.AsSpan().SequenceEqual(Zero4)) pages.Add(PagePassword);

        Info($"Resetting pages: {string.Join(", ", pages.Select(p => $"0x{p:X2}"))}...");

        var failures = new List<string>();
        var failedPages = new HashSet<int>();
        foreach (var page in pages)
        {
            try
            {
                await WritePageWithCenterFallbackAsync(tag, page, Zero4, ct);
            }
            catch (ToyPadCommandException ex)
            {
                failedPages.Add(page);
                failures.Add($"0x{page:X2}: write failed ({ex.Message})");
            }
        }

        foreach (var page in pages)
        {
            var readBack = await Device.ReadPageAsync(tag.Index, page, ct);
            if (!readBack.AsSpan().SequenceEqual(Zero4))
            {
                failedPages.Add(page);
                failures.Add($"0x{page:X2}: reads back {Convert.ToHexString(readBack)}");
            }
        }

        var gameplayOk = !failedPages.Overlaps(GameplayPages);
        var status = (gameplayOk, failedPages.Contains(PagePassword)) switch
        {
            (true, false) => ResetStatus.Success,
            (true, true) => ResetStatus.Partial,
            _ => ResetStatus.Failed
        };

        switch (status)
        {
            case ResetStatus.Success:
                Emit(LogLevel.Success, "Reset successful. All reset pages verify as 00000000.");
                _ = IdentifyAndUpdateAsync(tag);
                break;
            case ResetStatus.Partial:
                Emit(LogLevel.Warning, "Gameplay pages reset, but the password page had an issue.");
                break;
            default:
                Emit(LogLevel.Error, "Reset failed: verification mismatch on one or more pages.");
                break;
        }

        return new ResetResult(status, pages.Select(p => $"0x{p:X2}").ToList(), failures);
    }

    // ------------------------------------------------------------------

    private void Info(string message) => Emit(LogLevel.Info, message);

    private void Emit(LogLevel level, string message) => Log?.Invoke(new LogEntry(level, message, DateTimeOffset.Now));

    private void RaiseTagsChanged() => TagsChanged?.Invoke(ActiveTags);
}
