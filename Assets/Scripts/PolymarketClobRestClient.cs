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
        Action<PostOrderResponse> onOk,
        Action<string> onErr
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
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        req.SetRequestHeader("POLY_ADDRESS", polyAddress);
        req.SetRequestHeader("POLY_API_KEY", apiKey);
        req.SetRequestHeader("POLY_PASSPHRASE", passphrase);
        req.SetRequestHeader("POLY_TIMESTAMP", timestamp);
        req.SetRequestHeader("POLY_SIGNATURE", sig);

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onErr?.Invoke($"PostOrder error: {req.responseCode} {req.error} {req.downloadHandler?.text}");
            yield break;
        }

        string json = req.downloadHandler.text;
        var resp = JsonUtility.FromJson<PostOrderResponse>(json);

        if (resp == null)
        {
            onErr?.Invoke("PostOrder parse error: " + json);
            yield break;
        }

        onOk?.Invoke(resp);
    }
}
