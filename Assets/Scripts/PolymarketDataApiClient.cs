using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

// Read-only client for the public Polymarket Data API.
public class PolymarketDataApiClient
{
    private const string BaseUrl = "https://data-api.polymarket.com";
    private const int TimeoutSeconds = 10;

    // Size of the position in one outcome token for a wallet. Returns 0 when the wallet has no position.
    // userAddress is the wallet that holds the shares: the EOA, or the proxy/Safe (funder) address.
    public IEnumerator GetPositionSize(string userAddress, string tokenId, Action<decimal> onOk, Action<string> onErr)
    {
        string url = $"{BaseUrl}/positions?user={UnityWebRequest.EscapeURL(userAddress)}&limit=500";

        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = TimeoutSeconds;
            req.SetRequestHeader("Accept", "application/json");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onErr?.Invoke($"HTTP {req.responseCode} {req.error}");
                yield break;
            }

            DataApiPositionsResponse response;
            try
            {
                // JsonUtility cannot parse a top-level array, so wrap it in an object.
                response = JsonUtility.FromJson<DataApiPositionsResponse>("{\"positions\":" + req.downloadHandler.text + "}");
            }
            catch (Exception)
            {
                onErr?.Invoke("unexpected response format");
                yield break;
            }

            decimal total = 0m;
            foreach (DataApiPosition position in response?.positions ?? Array.Empty<DataApiPosition>())
            {
                if (position == null || position.asset != tokenId) continue;
                if (OrderAmountCalculator.TryToDecimal(position.size, out decimal size) && size > 0m)
                    total += size;
            }

            onOk?.Invoke(total);
        }
    }
}
