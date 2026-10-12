using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class PostOrderResponse
{
    public bool success;
    public string errorMsg;
    public string orderId;
    public string[] orderHashes;
    public string status;
}

public class PolymarketClobRestClient
{
    private const int PostOrderTimeoutSeconds = 15;

    private readonly string host;

    public PolymarketClobRestClient(string host)
    {
        this.host = host.TrimEnd('/');
    }

    public IEnumerator GetFeeRateBps(string tokenId, Action<string> onOk, Action<string> onErr)
    {
        string url = $"{host}/fee-rate?token_id={UnityWebRequest.EscapeURL(tokenId)}";

        using var req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onErr?.Invoke($"FeeRate error: {req.responseCode} {req.error} {req.downloadHandler?.text}");
            yield break;
        }

        // Keeps only the digits of the body. Breaks if the JSON ever has more than one number.
        string body = req.downloadHandler.text ?? "";
        string digits = "";
        foreach (char c in body) if (char.IsDigit(c)) digits += c;
        if (string.IsNullOrEmpty(digits)) digits = "0";

        onOk?.Invoke(digits);
    }

    public IEnumerator PostOrder(
        string apiKey,
        string secret,
        string passphrase,
        string polyAddress,
        string timestamp,
        string ownerApiKey,
        string orderJson, // full POST /order body: order, owner, orderType
        Action<OrderSendResult, PostOrderResponse, string> onDone // result, response (if any), error details
    )
    {
        string pathForSig = "/order";
        string url = host + pathForSig;

        string sig = PolymarketL2Auth.BuildL2Signature(
            secret: secret,
            timestamp: timestamp,
            method: "POST",
            requestPath: pathForSig,
            bodyOrNull: orderJson
        );

        byte[] bodyRaw = Encoding.UTF8.GetBytes(orderJson);

        using var req = new UnityWebRequest(url, "POST");
        req.timeout = PostOrderTimeoutSeconds;
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        req.SetRequestHeader("POLY_ADDRESS", polyAddress);
        req.SetRequestHeader("POLY_API_KEY", apiKey);
        req.SetRequestHeader("POLY_PASSPHRASE", passphrase);
        req.SetRequestHeader("POLY_TIMESTAMP", timestamp);
        req.SetRequestHeader("POLY_SIGNATURE", sig);

        yield return req.SendWebRequest();

        long status = req.responseCode; // 0 when no response arrived
        string body = req.downloadHandler?.text ?? "";

        PostOrderResponse resp = null;
        if (status >= 200 && status < 300)
        {
            try { resp = JsonUtility.FromJson<PostOrderResponse>(body); }
            catch (Exception) { resp = null; }
        }

        OrderSendResult result = OrderSendOutcome.Classify(status, resp != null, resp != null && resp.success);
        string details = result == OrderSendResult.Accepted ? null : $"HTTP {status} {req.error} {resp?.errorMsg ?? body}".Trim();
        onDone?.Invoke(result, resp, details);
    }
}
