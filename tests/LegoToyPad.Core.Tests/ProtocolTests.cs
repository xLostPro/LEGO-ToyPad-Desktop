using LegoToyPad.Core.Protocol;

namespace LegoToyPad.Core.Tests;

public class ProtocolTests
{
    [Fact]
    public void Encode_BuildsFrameWithChecksum()
    {
        var frame = PacketCodec.Encode(Commands.Read, 0x05, [0x00, 0x24]);

        Assert.Equal(32, frame.Length);
        Assert.Equal(new byte[] { 0x55, 0x04, 0xD2, 0x05, 0x00, 0x24 }, frame[..6]);
        Assert.Equal(unchecked((byte)(0x55 + 0x04 + 0xD2 + 0x05 + 0x24)), frame[6]);
        Assert.All(frame[7..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Decode_TagEvent()
    {
        var frame = new byte[32];
        frame[0] = 0x56;
        frame[2] = 2;
        frame[4] = 3;
        frame[5] = 1;
        Convert.FromHexString("04A1B2C3D4E580").CopyTo(frame, 6);

        var decoded = Assert.IsType<TagEventFrame>(PacketCodec.Decode(frame));
        Assert.Equal(new TagEventFrame(2, 3, false, "04A1B2C3D4E580"), decoded);
    }

    [Theory]
    [InlineData(0x00, true)]
    [InlineData(0x80, true)] // high nibble isn't an error on short acks
    [InlineData(0xF2, false)]
    public void Decode_ShortResponseStatus(byte status, bool ok)
    {
        var frame = new byte[32];
        frame[0] = 0x55;
        frame[1] = 0x02;
        frame[2] = 0x07;
        frame[3] = status;

        var decoded = Assert.IsType<ResponseFrame>(PacketCodec.Decode(frame));
        Assert.Equal(0x07, decoded.CommandId);
        Assert.False(decoded.IsReadResponse);
        Assert.Equal(ok, decoded.IsOk);
    }

    [Fact]
    public void Decode_ReadResponseCarries16Bytes()
    {
        var frame = new byte[32];
        frame[0] = 0x55;
        frame[1] = 0x12;
        frame[2] = 0x09;
        for (var i = 0; i < 16; i++) frame[4 + i] = (byte)(i + 1);

        var decoded = Assert.IsType<ResponseFrame>(PacketCodec.Decode(frame));
        Assert.True(decoded.IsReadResponse);
        Assert.Equal(Enumerable.Range(1, 16).Select(i => (byte)i), decoded.Data);
    }

    [Theory]
    [InlineData("04A1B2", "C3D4E580", "04A1B2C3D4E580")]
    [InlineData("000000", "00000000", null)]
    public void TryExtractUid_ValidatesBcc(string page0Prefix, string page1, string? expected)
    {
        var p0 = Convert.FromHexString(page0Prefix);
        var page0 = new byte[] { p0[0], p0[1], p0[2], (byte)(p0[0] ^ p0[1] ^ p0[2] ^ 0x88) };
        Assert.Equal(expected, TagUtils.TryExtractUid(page0, Convert.FromHexString(page1)));
    }

    [Fact]
    public void TryExtractUid_RejectsBadBcc()
    {
        Assert.Null(TagUtils.TryExtractUid([0x04, 0xA1, 0xB2, 0x00], [0xC3, 0xD4, 0xE5, 0x80]));
    }
}
