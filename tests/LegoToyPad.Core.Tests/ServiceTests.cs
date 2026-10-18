using LegoToyPad.Core.Crypto;
using LegoToyPad.Core.Transport;

namespace LegoToyPad.Core.Tests;

public class ServiceTests
{
    private const string Uid = "04A1B2C3D4E580";

    private static async Task<(ToyPadService Service, SimulatedToyPad Pad)> ConnectedAsync()
    {
        var pad = new SimulatedToyPad();
        var service = new ToyPadService(pad, startupDelay: TimeSpan.Zero);
        await service.ConnectAsync();
        return (service, pad);
    }

    private static async Task<ActiveTag> WaitForTagAsync(ToyPadService service, Func<ActiveTag, bool> predicate)
    {
        for (var i = 0; i < 200; i++)
        {
            var tag = service.ActiveTags.FirstOrDefault(predicate);
            if (tag is not null) return tag;
            await Task.Delay(10);
        }
        throw new TimeoutException("Tag never reached the expected state.");
    }

    [Fact]
    public async Task WriteCharacter_ThenRead_IdentifiesIt()
    {
        var (service, pad) = await ConnectedAsync();
        pad.PlaceTag(pad: 1, index: 0, Uid);
        await WaitForTagAsync(service, t => t.State == IdentifyState.Unrecognized);

        var check = await service.PrepareWriteAsync();
        Assert.True(check.Ok, check.Reason);

        var result = await service.CommitWriteAsync(TokenKind.Character, 1, check.Tag.Uid);
        Assert.True(result.Ok);
        Assert.Equal("Batman", result.Name);
        Assert.Equal("8539298E", Convert.ToHexString(pad.Page(0, 0x24)));

        var read = await service.ReadTagAsync(Pad.Center, 0);
        Assert.Equal(new TagIdentity(TokenKind.Character, 1, "Batman", "DC Comics"), read.Identity);
        Assert.False(read.Password.Matches); // blank custom tag, no password set

        var tag = await WaitForTagAsync(service, t => t.State == IdentifyState.Identified);
        Assert.Equal("Batman", tag.Identity!.Name);
    }

    [Fact]
    public async Task WriteCharacter_UsesUidFromTagMemory_NotTheEventUid()
    {
        var (service, pad) = await ConnectedAsync();
        pad.PlaceTag(pad: 1, index: 0, Uid, reportedUid: "04000000000000");
        await WaitForTagAsync(service, t => t.State != IdentifyState.Pending);

        await service.CommitWriteAsync(TokenKind.Character, 2);

        var expected = TagCrypto.EncryptCharacter(Uid, 2);
        Assert.Equal(expected[..4], pad.Page(0, 0x24));
        Assert.Equal(expected[4..], pad.Page(0, 0x25));
    }

    [Fact]
    public async Task WriteVehicle_FallsBackToCenterSlotOnF2()
    {
        var (service, pad) = await ConnectedAsync();
        pad.PlaceTag(pad: 1, index: 0, Uid);
        await WaitForTagAsync(service, t => t.State != IdentifyState.Pending);
        pad.RefuseWritesOn.Add(0);

        var result = await service.CommitWriteAsync(TokenKind.Vehicle, 1000);

        Assert.True(result.Ok);
        Assert.Equal("E8030000", Convert.ToHexString(pad.Page(0, 0x24)));
    }

    [Fact]
    public async Task PrepareWrite_RejectsPasswordProtectedTag()
    {
        var (service, pad) = await ConnectedAsync();
        pad.PlaceTag(pad: 1, index: 0, Uid, initialize: m => m[0x2B * 4] = 0x12);
        await WaitForTagAsync(service, t => t.State != IdentifyState.Pending);

        var check = await service.PrepareWriteAsync();

        Assert.False(check.Ok);
        Assert.Contains("non-zero password", check.Reason);
    }

    [Fact]
    public async Task Reset_ZeroesGameplayPages()
    {
        var (service, pad) = await ConnectedAsync();
        pad.PlaceTag(pad: 1, index: 0, Uid, initialize: m => m.AsSpan(0x24 * 4, 12).Fill(0xAB));
        await WaitForTagAsync(service, t => t.State != IdentifyState.Pending);

        var preview = await service.PrepareResetAsync();
        Assert.Equal("ABABABAB", preview.Current[0].Hex);

        var result = await service.CommitResetAsync(clearPasswordPage: false);

        Assert.Equal(ResetStatus.Success, result.Status);
        Assert.All(new[] { 0x24, 0x25, 0x26 }, p => Assert.Equal(new byte[4], pad.Page(0, p)));
    }

    [Fact]
    public async Task Rescan_FindsTagsAlreadyOnThePortal()
    {
        var pad = new SimulatedToyPad();
        pad.PlaceTag(pad: 2, index: 1, Uid, initialize: m => BitConverter.GetBytes(1000u).CopyTo(m, 0x24 * 4));
        var service = new ToyPadService(pad, startupDelay: TimeSpan.Zero);

        await service.ConnectAsync();

        var tag = Assert.Single(service.ActiveTags);
        Assert.Equal(Pad.Left, tag.Pad);
        Assert.Equal("Police Car", tag.Identity?.Name);
    }

    [Fact]
    public async Task TagRemoval_UpdatesActiveTags()
    {
        var (service, pad) = await ConnectedAsync();
        pad.PlaceTag(pad: 3, index: 4, Uid);
        await WaitForTagAsync(service, t => t.State != IdentifyState.Pending);

        pad.RemoveTag(pad: 3, index: 4);

        Assert.Empty(service.ActiveTags);
    }

    [Fact]
    public async Task Unplug_DisconnectsAndReportsError()
    {
        var (service, pad) = await ConnectedAsync();
        string? error = null;
        service.ConnectionChanged += (connected, e) => { if (!connected) error = e; };

        pad.Unplug();

        Assert.False(service.IsConnected);
        Assert.Equal("device removed", error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareWriteAsync());
    }

    [Fact]
    public async Task Commands_AreNeverInFlightConcurrently()
    {
        var (service, pad) = await ConnectedAsync();
        pad.PlaceTag(pad: 1, index: 0, Uid);
        pad.PlaceTag(pad: 2, index: 1, "04112233445566");
        pad.PlaceTag(pad: 3, index: 4, "04778899AABBCC");

        var reads = service.ActiveTags.Select(t => service.ReadTagAsync(t.Pad, t.Index));
        var results = await Task.WhenAll(reads.Append(service.ReadTagAsync(Pad.Center, 0)));

        Assert.Equal(4, results.Length);
        Assert.False(pad.OverlapDetected);
    }
}
