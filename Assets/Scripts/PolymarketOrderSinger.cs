using System.Numerics;
using System.Text;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;

public static class PolymarketOrderSigner
{
    // side: 0=BUY, 1=SELL
    public static string SignOrder(
        EthECKey key,
        int chainId,
        string maker,
        string signer,
        string taker,
        BigInteger tokenId,
        BigInteger makerAmount,
        BigInteger takerAmount,
        BigInteger expiration,
        BigInteger nonce,
        BigInteger feeRateBps,
        int side
    )
    {
        // Typed data as JSON to avoid depending on Nethereum's MemberValue API; uint values are strings.

        string json = BuildTypedDataJson(
            chainId, maker, signer, taker,
            tokenId, makerAmount, takerAmount,
            expiration, nonce, feeRateBps, side
        );

        var signer712 = new Eip712TypedDataSigner();
        return signer712.SignTypedDataV4(json, key);
    }

    private static string BuildTypedDataJson(
        int chainId,
        string maker,
        string signer,
        string taker,
        BigInteger tokenId,
        BigInteger makerAmount,
        BigInteger takerAmount,
        BigInteger expiration,
        BigInteger nonce,
        BigInteger feeRateBps,
        int side
    )
    {
        // No JSON escaping: all values are numbers or hex addresses.
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append("\"types\":{");
        sb.Append("\"EIP712Domain\":[");
        sb.Append("{\"name\":\"name\",\"type\":\"string\"},");
        sb.Append("{\"name\":\"version\",\"type\":\"string\"},");
        sb.Append("{\"name\":\"chainId\",\"type\":\"uint256\"},");
        sb.Append("{\"name\":\"verifyingContract\",\"type\":\"address\"}");
        sb.Append("],");
        sb.Append("\"Order\":[");
        sb.Append("{\"name\":\"salt\",\"type\":\"uint256\"},");
        sb.Append("{\"name\":\"maker\",\"type\":\"address\"},");
        sb.Append("{\"name\":\"signer\",\"type\":\"address\"},");
        sb.Append("{\"name\":\"taker\",\"type\":\"address\"},");
        sb.Append("{\"name\":\"tokenId\",\"type\":\"uint256\"},");
        sb.Append("{\"name\":\"makerAmount\",\"type\":\"uint256\"},");
        sb.Append("{\"name\":\"takerAmount\",\"type\":\"uint256\"},");
        sb.Append("{\"name\":\"expiration\",\"type\":\"uint256\"},");
        sb.Append("{\"name\":\"nonce\",\"type\":\"uint256\"},");
        sb.Append("{\"name\":\"feeRateBps\",\"type\":\"uint256\"},");
        sb.Append("{\"name\":\"side\",\"type\":\"uint8\"}");
        sb.Append("]");
        sb.Append("},");

        sb.Append("\"primaryType\":\"Order\",");
        sb.Append("\"domain\":{");
        sb.Append($"\"name\":\"{PolymarketConstants.ORDER_DOMAIN_NAME}\",");
        sb.Append($"\"version\":\"{PolymarketConstants.ORDER_DOMAIN_VERSION}\",");
        sb.Append($"\"chainId\":\"{chainId}\",");
        sb.Append($"\"verifyingContract\":\"{PolymarketConstants.EXCHANGE_VERIFYING_CONTRACT}\"");
        sb.Append("},");

        // salt is a placeholder "0"; PatchSalt sets the real value.
        sb.Append("\"message\":{");
        sb.Append("\"salt\":\"0\",");
        sb.Append($"\"maker\":\"{maker}\",");
        sb.Append($"\"signer\":\"{signer}\",");
        sb.Append($"\"taker\":\"{taker}\",");
        sb.Append($"\"tokenId\":\"{tokenId}\",");
        sb.Append($"\"makerAmount\":\"{makerAmount}\",");
        sb.Append($"\"takerAmount\":\"{takerAmount}\",");
        sb.Append($"\"expiration\":\"{expiration}\",");
        sb.Append($"\"nonce\":\"{nonce}\",");
        sb.Append($"\"feeRateBps\":\"{feeRateBps}\",");
        sb.Append($"\"side\":\"{side}\"");
        sb.Append("}");

        sb.Append("}");
        return sb.ToString();
    }

    public static string PatchSalt(string typedDataJson, BigInteger salt)
    {
        return typedDataJson.Replace("\"salt\":\"0\"", $"\"salt\":\"{salt}\"");
    }
}
