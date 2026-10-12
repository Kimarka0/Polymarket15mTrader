using System;
using System.Numerics;
using System.Text;

public static class PolymarketEip712OrderSigner
{
    // Domain from the CTFExchange contract: Hashing("Polymarket CTF Exchange", "1").
    public const string DOMAIN_NAME = "Polymarket CTF Exchange";
    public const string DOMAIN_VERSION = "1";

    // Signature types from the Polymarket docs.
    public const int SIG_TYPE_EOA = 0;
    public const int SIG_TYPE_POLY_PROXY = 1;
    public const int SIG_TYPE_POLY_SAFE = 2;

    public struct Order
    {
        public BigInteger salt;
        public string maker;
        public string signer;
        public string taker;
        public BigInteger tokenId;
        public BigInteger makerAmount;
        public BigInteger takerAmount;
        public BigInteger expiration;
        public BigInteger nonce;
        public BigInteger feeRateBps;
        public byte side; // 0 = BUY, 1 = SELL
    }

    public interface IEthSigner
    {
        // Returns the signature as hex "0x{r}{s}{v}".
        string SignDigest(byte[] digest32);
    }

    public static byte[] BuildOrderDigest(
        int chainId,
        string verifyingContract,
        Order o)
    {
        byte[] domainSeparator = BuildDomainSeparator(chainId, verifyingContract);
        byte[] orderHash = HashOrder(o);

        byte[] prefix = new byte[] { 0x19, 0x01 }; // EIP-191 prefix for typed data
        byte[] data = Concat(prefix, domainSeparator, orderHash);

        return Keccak256(data);
    }

    public static string SignOrder(
        IEthSigner signer,
        int chainId,
        string verifyingContract,
        Order o)
    {
        byte[] digest = BuildOrderDigest(chainId, verifyingContract, o);
        return signer.SignDigest(digest);
    }

    private static readonly byte[] DOMAIN_TYPEHASH =
        Keccak256(Encoding.UTF8.GetBytes("EIP712Domain(string name,string version,uint256 chainId,address verifyingContract)"));

    // Field order and types must match the contract exactly, or the signature is invalid.
    private static readonly byte[] ORDER_TYPEHASH =
        Keccak256(Encoding.UTF8.GetBytes(
            "Order(uint256 salt,address maker,address signer,address taker,uint256 tokenId,uint256 makerAmount,uint256 takerAmount,uint256 expiration,uint256 nonce,uint256 feeRateBps,uint8 side)"
        ));

    private static byte[] BuildDomainSeparator(int chainId, string verifyingContract)
    {
        byte[] nameHash = Keccak256(Encoding.UTF8.GetBytes(DOMAIN_NAME));
        byte[] versionHash = Keccak256(Encoding.UTF8.GetBytes(DOMAIN_VERSION));

        byte[] enc = AbiEncode(
            new AbiValue("bytes32", DOMAIN_TYPEHASH),
            new AbiValue("bytes32", nameHash),
            new AbiValue("bytes32", versionHash),
            new AbiValue("uint256", new BigInteger(chainId)),
            new AbiValue("address", verifyingContract)
        );

        return Keccak256(enc);
    }

    private static byte[] HashOrder(Order o)
    {
        byte[] enc = AbiEncode(
            new AbiValue("bytes32", ORDER_TYPEHASH),
            new AbiValue("uint256", o.salt),
            new AbiValue("address", o.maker),
            new AbiValue("address", o.signer),
            new AbiValue("address", o.taker),
            new AbiValue("uint256", o.tokenId),
            new AbiValue("uint256", o.makerAmount),
            new AbiValue("uint256", o.takerAmount),
            new AbiValue("uint256", o.expiration),
            new AbiValue("uint256", o.nonce),
            new AbiValue("uint256", o.feeRateBps),
            new AbiValue("uint8", new BigInteger(o.side))
        );

        return Keccak256(enc);
    }

    private struct AbiValue
    {
        public string type; // one of: uint256, address, bytes32, uint8
        public object value; // BigInteger, string or byte[]
        public AbiValue(string t, object v) { type = t; value = v; }
    }

    private static byte[] AbiEncode(params AbiValue[] values)
    {
        // Static types only: each value takes one 32-byte slot.
        byte[][] slots = new byte[values.Length][];
        for (int i = 0; i < values.Length; i++)
        {
            slots[i] = AbiEncodeOne(values[i]);
        }
        return Concat(slots);
    }

    private static byte[] AbiEncodeOne(AbiValue v)
    {
        if (v.type == "bytes32")
        {
            byte[] b = (byte[])v.value;
            if (b.Length != 32) throw new Exception("bytes32 must be 32 bytes");
            return b;
        }

        if (v.type == "address")
        {
            string addr = (string)v.value;
            byte[] a = HexToBytes(addr);
            if (a.Length != 20) throw new Exception("address must be 20 bytes");

            // Address is right-aligned in the 32-byte slot.
            byte[] slot = new byte[32];
            Buffer.BlockCopy(a, 0, slot, 12, 20);
            return slot;
        }

        if (v.type == "uint256" || v.type == "uint8")
        {
            BigInteger bi = (BigInteger)v.value;
            if (bi.Sign < 0) throw new Exception("uint cannot be negative");

            byte[] raw = bi.ToByteArray(isUnsigned: true, isBigEndian: true);
            if (raw.Length > 32) throw new Exception("uint too large");

            byte[] slot = new byte[32];
            Buffer.BlockCopy(raw, 0, slot, 32 - raw.Length, raw.Length);
            return slot;
        }

        throw new Exception("Unsupported ABI type: " + v.type);
    }

    private static byte[] Keccak256(byte[] input) => Keccak.Keccak256(input);

    private static byte[] Concat(params byte[][] parts)
    {
        int len = 0;
        for (int i = 0; i < parts.Length; i++) len += parts[i].Length;

        byte[] r = new byte[len];
        int off = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            Buffer.BlockCopy(parts[i], 0, r, off, parts[i].Length);
            off += parts[i].Length;
        }
        return r;
    }

    private static byte[] HexToBytes(string hex)
    {
        if (hex.StartsWith("0x")) hex = hex.Substring(2);
        if (hex.Length % 2 != 0) throw new Exception("hex length must be even");

        byte[] b = new byte[hex.Length / 2];
        for (int i = 0; i < b.Length; i++)
        {
            b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return b;
    }
}

// Unused stub that always throws. Use Keccak instead.
public static class keccak
{
    public static byte[] Keccak256(byte[] input)
    {
        throw new Exception("Keccak256 not implemented. Plug in a Keccak library (e.g., Nethereum.Util.Sha3Keccack).");
    }
}
