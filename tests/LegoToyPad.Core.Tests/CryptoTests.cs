using LegoToyPad.Core.Crypto;

namespace LegoToyPad.Core.Tests;

// Expected values were generated with the original CLI's crypto.js.
public class CryptoTests
{
    [Theory]
    [InlineData("04A1B2C3D4E580", 1u, "8539298E0A20DA08")]
    [InlineData("0456789ABCDEF0", 2u, "76ADD3D5C4ECADF5")]
    [InlineData("04123456789ABC", 86u, "28124E234E070CD8")]
    [InlineData("04FFEEDDCCBBAA", 42u, "9DB21EB44F2DA19B")]
    public void EncryptCharacter_MatchesCli(string uid, uint id, string expected)
    {
        Assert.Equal(expected, Convert.ToHexString(TagCrypto.EncryptCharacter(uid, id)));
    }

    [Theory]
    [InlineData("04A1B2C3D4E580", "6FA4B4BA6FBD43A9E012F9D001C5CC69")]
    [InlineData("04FFEEDDCCBBAA", "5734360F96415F8F0D295902F31C8473")]
    public void GenerateKey_MatchesCli(string uid, string expected)
    {
        Assert.Equal(expected, Convert.ToHexString(TagCrypto.GenerateKey(uid)));
    }

    [Theory]
    [InlineData("04A1B2C3D4E580", 0xBCC8A4B3u)]
    [InlineData("0456789ABCDEF0", 0xDD29B7F6u)]
    [InlineData("04123456789ABC", 0xD93F4EE5u)]
    [InlineData("04FFEEDDCCBBAA", 0xE27151EDu)]
    public void GeneratePassword_MatchesCli(string uid, uint expected)
    {
        Assert.Equal(expected, TagCrypto.GeneratePassword(uid));
    }

    [Fact]
    public void DecryptCharacter_RoundTrips()
    {
        var cipher = TagCrypto.EncryptCharacter("04A1B2C3D4E580", 77);
        Assert.Equal(77u, TagCrypto.DecryptCharacter("04A1B2C3D4E580", cipher));
    }
}
