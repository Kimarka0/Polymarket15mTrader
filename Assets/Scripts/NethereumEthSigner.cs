using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;

public class NethereumEthSigner : PolymarketEip712OrderSigner.IEthSigner
{
    private readonly EthECKey key;

    public NethereumEthSigner(string privateKeyHex)
    {
        key = new EthECKey(privateKeyHex);
    }

    public string SignDigest(byte[] digest32)
    {
        var sig = key.SignAndCalculateV(digest32);

        var r = sig.R.ToHex().PadLeft(64, '0');
        var s = sig.S.ToHex().PadLeft(64, '0');
        var v = sig.V[0].ToString("x2");

        return "0x" + r + s + v;
    }
}
