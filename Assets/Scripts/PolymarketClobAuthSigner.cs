using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Nethereum.Signer;
using Nethereum.Hex.HexConvertors.Extensions;

// EIP-712 L1 auth (ClobAuthDomain), hashed by hand without typed-data JSON.
public static class PolymarketClobAuthSigner
{
    // ClobAuthDomain has no verifyingContract, unlike the order domain.
    private static readonly byte[] TYPEHASH_EIP712_DOMAIN =
        Keccak.Keccak256(Encoding.UTF8.GetBytes("EIP712Domain(string name,string version,uint256 chainId)"));

    private static readonly byte[] TYPEHASH_CLOB_AUTH =
        Keccak.Keccak256(Encoding.UTF8.GetBytes("ClobAuth(address address,string timestamp,uint256 nonce,string message)"));

    public static byte[] BuildClobAuthDigest(int chainId, string signerAddress, string timestampString, BigInteger nonce, string message)
    {
        // domainSeparator = keccak256(typehash, keccak(name), keccak(version), chainId)
        byte[] nameHash = Keccak.Keccak256(Encoding.UTF8.GetBytes("ClobAuthDomain"));
        byte[] versionHash = Keccak.Keccak256(Encoding.UTF8.GetBytes("1"));

        byte[] domainEncoded = AbiEncodePacked32(
            TYPEHASH_EIP712_DOMAIN,
            nameHash,
            versionHash,
            EncodeUint256(chainId)
        );

        byte[] domainSeparator = Keccak.Keccak256(domainEncoded);

        // structHash = keccak256(typehash, address, keccak(timestamp), nonce, keccak(message)); order must match the type string.
        byte[] tsHash = Keccak.Keccak256(Encoding.UTF8.GetBytes(timestampString));
        byte[] msgHash = Keccak.Keccak256(Encoding.UTF8.GetBytes(message));

        byte[] structEncoded = AbiEncodePacked32(
            TYPEHASH_CLOB_AUTH,
            EncodeAddress(signerAddress),
            tsHash,
            EncodeUint256(nonce),
            msgHash
        );

        byte[] structHash = Keccak.Keccak256(structEncoded);

        // digest = keccak256(0x19 0x01 || domainSeparator || structHash)
        byte[] prefix = new byte[] { 0x19, 0x01 };
        byte[] digestInput = Concat(prefix, domainSeparator, structHash);
        return Keccak.Keccak256(digestInput);
    }

    public static string SignClobAuth(int chainId, EthECKey key, string signerAddress, string timestampString, BigInteger nonce, string message)
    {
        byte[] digest = BuildClobAuthDigest(chainId, signerAddress, timestampString, nonce, message);

        // SignAndCalculateV expects the 32-byte digest, not the message.
        var sig = key.SignAndCalculateV(digest);

        string r = sig.R.ToHex().PadLeft(64, '0');
        string s = sig.S.ToHex().PadLeft(64, '0');
        string v = sig.V[0].ToString("x2");

        return "0x" + r + s + v;
    }

    // abi.encode of static 32-byte words is plain concatenation.
    private static byte[] AbiEncodePacked32(params byte[][] words32)
    {
        int total = 0;
        foreach (var w in words32)
        {
            if (w == null || w.Length != 32) throw new Exception("ABI word must be 32 bytes");
            total += 32;
        }

        byte[] result = new byte[total];
        int offset = 0;
        foreach (var w in words32)
        {
            Buffer.BlockCopy(w, 0, result, offset, 32);
            offset += 32;
        }
        return result;
    }

    private static byte[] EncodeUint256(BigInteger value)
    {
        if (value < 0) throw new Exception("uint256 cannot be negative");
        byte[] raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);

        byte[] out32 = new byte[32];
        int copyLen = Math.Min(raw.Length, 32);
        Buffer.BlockCopy(raw, raw.Length - copyLen, out32, 32 - copyLen, copyLen);
        return out32;
    }

    private static byte[] EncodeUint256(int value)
    {
        return EncodeUint256(new BigInteger(value));
    }

    private static byte[] EncodeAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new Exception("address is null/empty");
        if (!address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) throw new Exception("address must start with 0x");
        if (address.Length != 42) throw new Exception("address must be 42 chars (0x + 40 hex)");

        byte[] addr20 = address.HexToByteArray();
        if (addr20.Length != 20) throw new Exception("address bytes must be 20");

        byte[] out32 = new byte[32];
        // ABI encodes an address right-aligned: 12 zero bytes, then the 20 address bytes.
        Buffer.BlockCopy(addr20, 0, out32, 12, 20);
        return out32;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int total = 0;
        foreach (var p in parts) total += p.Length;
        byte[] r = new byte[total];
        int o = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, r, o, p.Length);
            o += p.Length;
        }
        return r;
    }
}
