using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System.Globalization;

public class ApiManager : MonoBehaviour
{
    private const string GAMMA_API_BASE = "https://gamma-api.polymarket.com";
    private const string CLOB_API_BASE = "https://clob.polymarket.com";

    private IEnumerator GetExactHistoricalPrice(DateTime targetTime, Action<double> onSuccess, Action<string> onError)
    {
        DateTime candleStartTime = new DateTime(
            targetTime.Year, targetTime.Month, targetTime.Day, 
            targetTime.Hour, targetTime.Minute, 0, DateTimeKind.Utc
        );

        long startTimestamp = ((DateTimeOffset)candleStartTime).ToUnixTimeMilliseconds();

        string url = $"https://api.binance.com/api/v3/klines?symbol=BTCUSDT&interval=1m&startTime={startTimestamp}&limit=1";

        Debug.Log($"[PriceFetch] Requesting exact candle for: {candleStartTime:HH:mm:ss}");

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PriceFetch] Failed to get exact candle: {request.error}");
                onError?.Invoke("Failed to fetch historical price");
                yield break;
            }

            string json = request.downloadHandler.text;

            if (json == "[]" || string.IsNullOrEmpty(json))
            {
                Debug.Log("[PriceFetch] No exact candle found, falling back to current price");
                yield return StartCoroutine(GetCurrentBTCPrice(onSuccess, onError));
                yield break;
            }

            try
            {
                // Binance kline format: [[openTime, "open", ...]]; the open price is the second element.
                string cleanJson = json.Trim('[', ']');
                if (string.IsNullOrEmpty(cleanJson))
                {
                    onError?.Invoke("Empty candle data");
                    yield break;
                }

                string[] candleData = cleanJson.Split(',');
                if (candleData.Length >= 2)
                {
                    string openPriceStr = candleData[1].Trim('"');

                    if (double.TryParse(openPriceStr, NumberStyles.Float, 
                        CultureInfo.InvariantCulture, out double historicalPrice))
                    {
                        Debug.Log($"[PriceFetch] Exact historical price found: ${historicalPrice:F2}");
                        onSuccess?.Invoke(historicalPrice);
                    }
                    else
                    {
                        onError?.Invoke($"Failed to parse price: {openPriceStr}");
                    }
                }
                else
                {
                    onError?.Invoke("Invalid candle data format");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PriceFetch] Error parsing response: {e.Message}");
                onError?.Invoke($"Parsing error: {e.Message}");
            }
        }
    }

    public IEnumerator GetPriceAtMarketStart(DateTime marketStartTime, Action<double> onSuccess, Action<string> onError)
    {
        Debug.Log($"[PriceFetch] Requesting BTC price at market start: {marketStartTime:HH:mm:ss}");

        yield return StartCoroutine(GetExactHistoricalPrice(marketStartTime, 
            onSuccess: (price) => {
                onSuccess?.Invoke(price);
            },
            onError: (error) => {
                Debug.Log($"[PriceFetch] Historical price failed: {error}, trying fallback...");
                StartCoroutine(GetCurrentBTCPrice(onSuccess, onError));
            }
        ));
    }
    public IEnumerator GetMarketById(
    string marketId,
    Action<Market> onSuccess,
    Action<string> onError
)
{
    string url = GAMMA_API_BASE + "/markets/" + marketId;

    using (UnityWebRequest req = UnityWebRequest.Get(url))
    {
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(req.error);
            yield break;
        }

        Market market = JsonUtility.FromJson<Market>(req.downloadHandler.text);
        onSuccess?.Invoke(market);
    }
}

    public IEnumerator GetCurrentBTCPrice(Action<double> onSuccess, Action<string> onError)
    {
        string url = "https://api.binance.com/api/v3/ticker/price?symbol=BTCUSDT";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = request.downloadHandler.text;

                BinancePriceResponse response = JsonUtility.FromJson<BinancePriceResponse>(json);
                if (response != null && !string.IsNullOrEmpty(response.price))
                {
                    if (double.TryParse(response.price, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double price))
                    {
                        onSuccess?.Invoke(price);
                    }
                    else
                    {
                        onError?.Invoke("Failed to parse price");
                    }
                }
                else
                {
                    onError?.Invoke("Empty response from Binance");
                }
            }
            else
            {
                onError?.Invoke($"Request error: {request.error}");
            }
        }
    }
    public IEnumerator GetOrderBook(
    string tokenId,
    Action<ClobOrderBook> onSuccess,
    Action<string> onError
)
{
    if (string.IsNullOrEmpty(tokenId))
    {
        onError?.Invoke("TokenId is empty");
        yield break;
    }

    string url = CLOB_API_BASE + "/book?token_id=" + tokenId;

    using (UnityWebRequest req = UnityWebRequest.Get(url))
    {
        req.SetRequestHeader("Accept", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(req.error);
            yield break;
        }

        ClobOrderBook book =
            JsonUtility.FromJson<ClobOrderBook>(req.downloadHandler.text);

        onSuccess?.Invoke(book);
    }
}
public IEnumerator GetMarketBySlug(
    string slug,
    Action<Market> onSuccess,
    Action<string> onError
)
{
    string url = GAMMA_API_BASE + "/markets?slug=" + slug;

    using (UnityWebRequest req = UnityWebRequest.Get(url))
    {
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(req.error);
            yield break;
        }

        string wrapped = "{ \"markets\": " + req.downloadHandler.text + " }";
        var resp = JsonUtility.FromJson<GammaMarketsResponse>(wrapped);

        if (resp.markets != null && resp.markets.Length > 0)
            onSuccess?.Invoke(resp.markets[0]);
        else
            onError?.Invoke("No markets found for slug");
    }
}

    public IEnumerator GetEventBySlug(string slug, Action<PolymarketEvent> onSuccess, Action<string> onError)
    {
        string url = $"{GAMMA_API_BASE}/events/slug/{slug}";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"Event not found: {request.error}");
                yield break;
            }

            string json = request.downloadHandler.text;

            PolymarketEvent marketEvent = JsonUtility.FromJson<PolymarketEvent>(json);

            if (marketEvent != null && !string.IsNullOrEmpty(marketEvent.id))
            {
                onSuccess?.Invoke(marketEvent);
            }
            else
            {
                onError?.Invoke("Empty response");
            }
        }
    }

    private IEnumerator GetHistoricalPriceSafe(DateTime targetTime, Action<double> onSuccess, Action<string> onError)
    {
        DateTime candleStartTime = new DateTime(
            targetTime.Year, targetTime.Month, targetTime.Day, 
            targetTime.Hour, targetTime.Minute, 0, DateTimeKind.Utc
        );

        long startTimestamp = ((DateTimeOffset)candleStartTime).ToUnixTimeMilliseconds();
        string url = $"https://api.binance.com/api/v3/klines?symbol=BTCUSDT&interval=1m&startTime={startTimestamp}&limit=1";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            bool hasError = false;
            string errorMessage = "";

            if (request.result != UnityWebRequest.Result.Success)
            {
                hasError = true;
                errorMessage = $"Request failed: {request.error}";
            }

            string json = request.downloadHandler.text;

            if (!hasError && (json == "[]" || string.IsNullOrEmpty(json)))
            {
                hasError = true;
                errorMessage = "No candle data available";
            }

            if (hasError)
            {
                onError?.Invoke(errorMessage);
                yield break;
            }

            string cleanJson = json.Trim('[', ']');
            string[] candleData = cleanJson.Split(',');

            if (candleData.Length >= 2)
            {
                string openPriceStr = candleData[1].Trim('"');

                if (double.TryParse(openPriceStr, NumberStyles.Float, 
                    CultureInfo.InvariantCulture, out double historicalPrice))
                {
                    Debug.Log($"[PriceFetch] Historical price: ${historicalPrice:F2}");
                    onSuccess?.Invoke(historicalPrice);
                }
                else
                {
                    onError?.Invoke("Failed to parse price value");
                }
            }
            else
            {
                onError?.Invoke("Invalid data format");
            }
        }
    }

    public IEnumerator FindActive15MinuteBtcMarket(Action<PolymarketEvent> onSuccess, Action<string> onError)
    {
        Debug.Log("[MarketSearch] Starting search for active 15-minute BTC market");
        yield return StartCoroutine(SearchByGeneratedSlug(onSuccess, onError));
    }

    private IEnumerator SearchByGeneratedSlug(Action<PolymarketEvent> onSuccess, Action<string> onError)
    {
        string currentTimestamp = GetCurrent15MinTimestamp();
        string[] possibleSlugs = MarketInterval.GetSlugCandidates(currentTimestamp);

        foreach (var slug in possibleSlugs)
        {
            bool searchCompleted = false;
            PolymarketEvent foundEvent = null;

            yield return StartCoroutine(GetEventBySlug(slug,
                onSuccess: (market) => {
                    foundEvent = market;
                    searchCompleted = true;
                },
                onError: (err) => {
                    searchCompleted = true;
                }
            ));

            float timeout = 3f;
            while (!searchCompleted && timeout > 0)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (foundEvent != null && foundEvent.active && !foundEvent.closed)
            {
                onSuccess?.Invoke(foundEvent);
                yield break;
            }
        }

        yield return StartCoroutine(SearchInEventsList(onSuccess, onError));
    }

    private IEnumerator SearchInEventsList(Action<PolymarketEvent> onSuccess, Action<string> onError)
    {
        string url = $"{GAMMA_API_BASE}/events?active=true&closed=false&limit=100";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"API Error: {request.error}");
                yield break;
            }

            string json = request.downloadHandler.text;
            string wrappedJson = "{\"events\":" + json + "}";
            GammaEventsResponse response = JsonUtility.FromJson<GammaEventsResponse>(wrappedJson);

            if (response.events == null || response.events.Length == 0)
            {
                onError?.Invoke("No active events");
                yield break;
            }

            PolymarketEvent targetMarket = FindBtc15MinMarket(response.events);

            if (targetMarket != null)
            {
                onSuccess?.Invoke(targetMarket);
            }
            else
            {
                onError?.Invoke("BTC 15min Up/Down market not found");
            }
        }
    }
    public IEnumerator GetTradesByCondition(
    string conditionId,
    int limit,
    int offset,
    Action<DataApiTrade[]> onSuccess,
    Action<string> onError
)
{
    // Data API takes the conditionId (0x + 64 hex) as the market parameter.
    string url =
        $"https://data-api.polymarket.com/trades?market={UnityWebRequest.EscapeURL(conditionId)}" +
        $"&limit={limit}&offset={offset}&takerOnly=false";

    using (var req = UnityWebRequest.Get(url))
    {
        req.SetRequestHeader("Accept", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke($"HTTP {(int)req.responseCode} {req.error} | {req.downloadHandler.text}");
            yield break;
        }

        string json = req.downloadHandler.text;

        // JsonUtility cannot parse a top-level array, so wrap it in an object.
        string wrapped = "{\"trades\":" + json + "}";
        var data = JsonUtility.FromJson<DataApiTradesResponse>(wrapped);

        onSuccess?.Invoke(data?.trades ?? Array.Empty<DataApiTrade>());
    }
}

    private string GetCurrent15MinTimestamp()
    {
        return MarketInterval.GetSlugTimestamp(DateTime.UtcNow);
    }

    private PolymarketEvent FindBtc15MinMarket(PolymarketEvent[] events)
    {
        if (events == null) return null;

        foreach (var evt in events)
        {
            if (evt == null || !evt.active || evt.closed) continue;

            string title = (evt.title ?? "").ToLower();
            string slug = (evt.slug ?? "").ToLower();

            bool isBtc = title.Contains("btc") || title.Contains("bitcoin") || 
                        slug.Contains("btc") || slug.Contains("bitcoin");
            if (!isBtc) continue;

            bool is15Min = title.Contains("15") || title.Contains("15m") || 
                          slug.Contains("15") || slug.Contains("15m");
            bool isUpDown = (title.Contains("up") && title.Contains("down")) ||
                           title.Contains("up/down") || title.Contains("up or down") ||
                           (slug.Contains("up") && slug.Contains("down"));

            if (is15Min && isUpDown) return evt;
        }

        return null;
    }

    public PolymarketEvent GetTestMarket()
    {
        string timestamp = GetCurrent15MinTimestamp();
        DateTime startTime = DateTime.UtcNow.AddMinutes(-5);
        DateTime endTime = startTime.AddMinutes(15);

        return new PolymarketEvent
        {
            id = "test_btc_15m_" + timestamp,
            title = "Bitcoin Up or Down [15min Test]",
            slug = "btc-updown-15m-test-" + timestamp,
            active = true,
            closed = false,
            startDate = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            endDate = endTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            markets = new Market[] {
                new Market {
                    id = "test_market_" + timestamp,
                    question = "Will Bitcoin price be higher than current price in 15 minutes?",
                    active = true,
                    closed = false
                }
            }
        };
    }
}
