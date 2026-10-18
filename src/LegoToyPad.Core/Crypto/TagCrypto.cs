using System.Buffers.Binary;
using System.Numerics;

namespace LegoToyPad.Core.Crypto;

/// <summary>
/// Character/password crypto used by LEGO Dimensions NTAG213 tags.
/// Ported from the CLI's crypto.js: a UID-keyed TEA cipher for character
/// IDs (pages 0x24/0x25) and a UID-derived password for page 0x2B.
/// </summary>
public static class TagCrypto
{
    private const int TeaRounds = 32;
    private const uint TeaDelta = 0x9E3779B9;

    private static readonly byte[] KeyMixTemplate =
    [
        0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xB7,
        0xD5, 0xD7, 0xE6, 0xE7,
        0xBA, 0x3C, 0xA8, 0xD8,
        0x75, 0x47, 0x68, 0xCF,
        0x23, 0xE9, 0xFE, 0xAA
    ];

    /// <summary>Encrypts a character ID for the given 7-byte UID (14 hex chars). Returns 8 bytes: page 0x24 then 0x25.</summary>
    public static byte[] EncryptCharacter(string uidHex, uint characterId)
    {
        var key = GenerateKey(uidHex);
        var block = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(0), characterId);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), characterId);
        return TeaEncrypt(key, block);
    }

    /// <summary>Decrypts pages 0x24/0x25 back to a character ID.</summary>
    public static uint DecryptCharacter(string uidHex, ReadOnlySpan<byte> cipherText)
    {
        var plain = TeaDecrypt(GenerateKey(uidHex), cipherText);
        return BinaryPrimitives.ReadUInt32LittleEndian(plain);
    }

    /// <summary>The password the game expects on page 0x2B for a given UID.</summary>
    public static uint GeneratePassword(string uidHex)
    {
        var seed = new byte[32];
        Array.Fill(seed, (byte)0xFF);
        seed[0] = 0x09;
        Convert.FromHexString(uidHex).CopyTo(seed, 1);
        return RunMixer(seed, 8);
    }

    internal static byte[] GenerateKey(string uidHex)
    {
        var uid = Convert.FromHexString(uidHex);
        var key = new byte[16];
        for (var i = 0; i < 4; i++)
        {
            var rounds = i + 3;
            var seed = (byte[])KeyMixTemplate.Clone();
            uid.CopyTo(seed, 0);
            seed[rounds * 4 - 1] = 0xAA;
            BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(i * 4), RunMixer(seed, rounds));
        }
        return key;
    }

    private static uint RunMixer(ReadOnlySpan<byte> seed, int rounds)
    {
        uint acc = 0;
        for (var i = 0; i < rounds; i++)
        {
            var word = BinaryPrimitives.ReadUInt32LittleEndian(seed[(i * 4)..]);
            var blend = BitOperations.RotateRight(acc, 25) + BitOperations.RotateRight(acc, 10);
            acc = word + blend - acc;
        }
        return acc;
    }

    private static (uint K0, uint K1, uint K2, uint K3) KeyWords(ReadOnlySpan<byte> key) => (
        BinaryPrimitives.ReadUInt32LittleEndian(key),
        BinaryPrimitives.ReadUInt32LittleEndian(key[4..]),
        BinaryPrimitives.ReadUInt32LittleEndian(key[8..]),
        BinaryPrimitives.ReadUInt32LittleEndian(key[12..]));

    private static byte[] TeaEncrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> block)
    {
        var (k0, k1, k2, k3) = KeyWords(key);
        var left = BinaryPrimitives.ReadUInt32LittleEndian(block);
        var right = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);

        uint sum = 0;
        for (var i = 0; i < TeaRounds; i++)
        {
            sum += TeaDelta;
            left += ((right << 4) + k0) ^ (right + sum) ^ ((right >> 5) + k1);
            right += ((left << 4) + k2) ^ (left + sum) ^ ((left >> 5) + k3);
        }

        return Pack(left, right);
    }

    private static byte[] TeaDecrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> block)
    {
        var (k0, k1, k2, k3) = KeyWords(key);
        var left = BinaryPrimitives.ReadUInt32LittleEndian(block);
        var right = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);

        var sum = unchecked(TeaDelta * TeaRounds);
        for (var i = 0; i < TeaRounds; i++)
        {
            right -= ((left << 4) + k2) ^ (left + sum) ^ ((left >> 5) + k3);
            left -= ((right << 4) + k0) ^ (right + sum) ^ ((right >> 5) + k1);
            sum -= TeaDelta;
        }

        return Pack(left, right);
    }

    private static byte[] Pack(uint left, uint right)
    {
        var output = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(output, left);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), right);
        return output;
    }
}
