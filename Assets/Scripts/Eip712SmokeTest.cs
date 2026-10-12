using UnityEngine;
using System.Numerics;

public class Eip712SmokeTest : MonoBehaviour
{
    [SerializeField] private string privateKey; // test key only, never a real wallet
    [SerializeField] private int chainId = 137; // Polygon
    [SerializeField] private string exchange = "0x4bFb41d5B3570DeFd03C39a9A4D8dE6Bd8B8982E";

    void Start()
    {
        var signer = new NethereumEthSigner(privateKey);

        var o = new PolymarketEip712OrderSigner.Order
        {
            salt = new BigInteger(123),
            maker = "0x0000000000000000000000000000000000000001",
            signer = "0x0000000000000000000000000000000000000001",
            taker = "0x0000000000000000000000000000000000000000",
            tokenId = new BigInteger(1),
            makerAmount = new BigInteger(1000),
            takerAmount = new BigInteger(2000),
            expiration = new BigInteger(9999999999),
            nonce = new BigInteger(0),
            feeRateBps = new BigInteger(0),
            side = 0
        };

        var digest = PolymarketEip712OrderSigner.BuildOrderDigest(chainId, exchange, o);
        Debug.Log("digest: 0x" + BytesToHex(digest));

        var sig = PolymarketEip712OrderSigner.SignOrder(signer, chainId, exchange, o);
        Debug.Log("signature: " + sig);
    }

    private static string BytesToHex(byte[] b)
    {
        const string hex = "0123456789abcdef";
        char[] c = new char[b.Length * 2];
        for (int i = 0; i < b.Length; i++)
        {
            c[i * 2] = hex[b[i] >> 4];
            c[i * 2 + 1] = hex[b[i] & 0xF];
        }
        return new string(c);
    }
}
