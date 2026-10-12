using System;
using System.Collections;
using UnityEngine;

public class MarketController : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float logInterval = 30f;
    [SerializeField] private float updateInterval = 1f;
    [SerializeField] private float marketSearchDelay = 5f; 

    [Header("Test Data")]
    [SerializeField] private double testStartPrice = 50000.0;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;

    private MarketState marketState;
    private ApiManager apiManager;
    private Coroutine marketSearchCoroutine;
    private Coroutine dataUpdateCoroutine;
    private Coroutine marketResetCoroutine;
    private Coroutine orderBookCoroutine;
    private string currentConditionId;
    private Coroutine tradesUpdateCoroutine;

    private float logTimer = 0f;
    private bool isTrading = false;
    private int searchCount = 0;
    private int currentMarketId = 0;
    public MarketState GetMarketStateSafe() => marketState;

    void Start()
    {
        Debug.Log("[MarketController] 15-minute BTC trading script initialized");

        marketState = new MarketState();

        apiManager = GetComponent<ApiManager>();
        if (apiManager == null)
        {
            apiManager = gameObject.AddComponent<ApiManager>();
        }

        if (UIDisplay.Instance != null)
        {
            UIDisplay.Instance.SetInfoMessage("WAITING FOR START");
        }
    }

    void Update()
    {
        if (!isTrading || !marketState.IsInitialized) return;

        logTimer += Time.deltaTime;
        if (logTimer >= logInterval)
        {
            logTimer = 0f;
            LogMarketData();
        }
    }
    private void StartTradesUpdates()
{
    if (tradesUpdateCoroutine != null)
        StopCoroutine(tradesUpdateCoroutine);

    tradesUpdateCoroutine = StartCoroutine(TradesUpdateLoop());
}

    public void StartMonitoring()
    {
        if (isTrading)
        {
            Debug.Log("[MarketController] Monitoring already started");
            return;
        }

        Debug.Log("[MarketController] Starting market monitoring");
        isTrading = true;
        currentMarketId++;

        if (UIDisplay.Instance != null)
        {
            UIDisplay.Instance.SetInfoMessage("SEARCHING MARKET");
        }

        StartMarketResetCycle();

        if (marketSearchCoroutine != null)
        {
            StopCoroutine(marketSearchCoroutine);
        }
        marketSearchCoroutine = StartCoroutine(FindAndSetupMarket());
    }

    public void StopMonitoring()
    {
        if (!isTrading)
        {
            Debug.Log("[MarketController] Monitoring already stopped");
            return;
        }

        Debug.Log("[MarketController] Stopping monitoring");
        isTrading = false;

        if (marketSearchCoroutine != null)
        {
            StopCoroutine(marketSearchCoroutine);
            marketSearchCoroutine = null;
        }

        if (dataUpdateCoroutine != null)
        {
            StopCoroutine(dataUpdateCoroutine);
            dataUpdateCoroutine = null;
        }

        if (marketResetCoroutine != null)
        {
            StopCoroutine(marketResetCoroutine);
            marketResetCoroutine = null;
        }

        marketState.SetInitialized(false);

        if (UIDisplay.Instance != null)
        {
            UIDisplay.Instance.SetInfoMessage("MONITORING STOPPED");
            UIDisplay.Instance.ResetDisplay();
        }
        if (orderBookCoroutine != null)
{
    StopCoroutine(orderBookCoroutine);
    orderBookCoroutine = null;
}

    }

    private IEnumerator FindAndSetupMarket()
    {
        while (isTrading)
        {
            searchCount++;
            // The counter changes when the market is reset; if it changed while we waited,
            // this search is stale and must not touch the state.
            int thisMarketId = currentMarketId;

            Debug.Log($"[MarketController] Market search (attempt #{searchCount}, market #{thisMarketId})");

            if (UIDisplay.Instance != null)
            {
                UIDisplay.Instance.SetInfoMessage($"SEARCH #{searchCount}");
            }

            PolymarketEvent foundMarket = null;
            bool searchCompleted = false;

            yield return StartCoroutine(apiManager.FindActive15MinuteBtcMarket(
                onSuccess: (market) => {
                    foundMarket = market;
                    searchCompleted = true;
                    if (enableDebugLogs && market != null)
                        Debug.Log($"[MarketController] Found market: {market.title}");
                },
                onError: (error) => {
                    if (enableDebugLogs) Debug.Log($"[MarketController] Search error: {error}");
                    searchCompleted = true;
                }
            ));

            float timeout = 10f;
            while (!searchCompleted && timeout > 0 && isTrading)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (foundMarket == null && isTrading)
            {
                Debug.Log("[MarketController] Using test market");
                foundMarket = apiManager.GetTestMarket();

                if (UIDisplay.Instance != null)
                {
                    UIDisplay.Instance.SetInfoMessage("TEST MODE");
                }
            }

            if (foundMarket != null && isTrading && thisMarketId == currentMarketId)
            {
                yield return StartCoroutine(SetupMarketWithHistoricalPrice(foundMarket));

                while (marketState.IsMarketActive() && isTrading && thisMarketId == currentMarketId)
                {
                    yield return new WaitForSeconds(1f);
                }

                if (isTrading && thisMarketId == currentMarketId)
                {
                    OnMarketCompleted();

                    Debug.Log($"[MarketController] Waiting {marketSearchDelay}s before next market...");
                    yield return new WaitForSeconds(marketSearchDelay);
                }
            }
            else if (isTrading && thisMarketId == currentMarketId)
            {
                Debug.Log("[MarketController] Failed to find market, retrying in 10 seconds");
                yield return new WaitForSeconds(10f);
            }
        }
    }

    private IEnumerator SetupMarketWithHistoricalPrice(PolymarketEvent market)
{
    Debug.Log($"[MarketController] Setting up market: {market.title}");

    marketState.UpdateMarketInfo(market);

    // Get the token ids first. Callbacks cannot yield, so results go through locals.
    string upToken = null;
    string downToken = null;
    bool marketLoaded = false;

    yield return StartCoroutine(apiManager.GetMarketBySlug(
        market.slug,
        m =>
        {
            var tokens = m.GetClobTokenIdsNormalized();
            marketState.conditionId = m.conditionId;

            Debug.Log("[MarketController] clobTokenIds count: " + (tokens?.Length ?? 0));

            if (tokens != null && tokens.Length == 2)
            {
                upToken = tokens[0];
                downToken = tokens[1];
                Debug.Log("[MarketController] UP token: " + upToken);
                Debug.Log("[MarketController] DOWN token: " + downToken);
            }
            else
            {
                Debug.LogWarning("[MarketController] CLOB tokens not ready yet");
            }

            marketLoaded = true;
        },
        err =>
        {
            Debug.LogError("[MarketController] GetMarketBySlug error: " + err);
            marketLoaded = true;
        }
    ));

    float timeout = 8f;
    while (!marketLoaded && timeout > 0f)
    {
        timeout -= Time.deltaTime;
        yield return null;
    }

    if (string.IsNullOrEmpty(upToken) || string.IsNullOrEmpty(downToken))
    {
        Debug.LogError("[MarketController] Failed to get CLOB tokenIds. Abort setup.");
        yield break;
    }

    marketState.upTokenId = upToken;
    marketState.downTokenId = downToken;
    currentConditionId = marketState.conditionId;

    StartOrderBookUpdates();
    StartTradesUpdates();   

    DateTime marketStartTime = marketState.Current.sessionStartTime;
    Debug.Log($"[MarketController] Market start: {marketStartTime:HH:mm:ss}");
    Debug.Log($"[MarketController] Market end: {marketState.Current.sessionEndTime:HH:mm:ss}");

    yield return StartCoroutine(GetHistoricalStartingPrice(marketStartTime));

    if (!marketState.IsInitialized)
    {
        Debug.LogError("[MarketController] Failed to initialize market state");
        yield break;
    }

    StartDataUpdates();
    UpdateUI();

    Debug.Log($"[MARKET START] {marketState.Current.eventTitle} | Interval: {marketState.Current.sessionStartTime:HH:mm:ss} - {marketState.Current.sessionEndTime:HH:mm:ss} | Target: ${marketState.Current.priceToBeatChainlink:F2}");

    TimeSpan timeSinceStart = DateTime.UtcNow - marketStartTime;
    TimeSpan timeRemaining = marketState.Current.sessionEndTime - DateTime.UtcNow;

    Debug.Log($"[MarketController] Market started {timeSinceStart.TotalMinutes:F1} minutes ago");
    Debug.Log($"[MarketController] Time remaining: {timeRemaining.TotalMinutes:F1} minutes");
    Debug.Log($"[MarketController] Target price: ${marketState.Current.priceToBeatChainlink:F2}");

    if (UIDisplay.Instance != null)
    {
        UIDisplay.Instance.SetInfoMessage("MARKET ACTIVE");
    }
}

    private void StartOrderBookUpdates()
{
    if (orderBookCoroutine != null)
        StopCoroutine(orderBookCoroutine);

    orderBookCoroutine = StartCoroutine(OrderBookUpdateLoop());
}

    private IEnumerator OrderBookUpdateLoop()
    {
        Debug.Log("[CLOB] OrderBookUpdateLoop started");

        while (isTrading)
        {
            if (!string.IsNullOrEmpty(marketState.upTokenId))
            {
                yield return StartCoroutine(apiManager.GetOrderBook(
                    marketState.upTokenId,
                    b =>
                    {
                        marketState.upOrderBook = b;

                        if (string.IsNullOrEmpty(currentConditionId) && b != null && !string.IsNullOrEmpty(b.market))
                         currentConditionId = b.market; 

                        Debug.Log("[CLOB] UP book loaded");
                    },
                    err => Debug.LogError("[CLOB] UP error: " + err)
                ));
            }

            if (!string.IsNullOrEmpty(marketState.downTokenId))
            {
                yield return StartCoroutine(apiManager.GetOrderBook(
                    marketState.downTokenId,
                    b =>
                    {
                        marketState.downOrderBook = b;
                        Debug.Log("[CLOB] DOWN book loaded");
                    },
                    err => Debug.LogError("[CLOB] DOWN error: " + err)
                ));
            }

            yield return new WaitForSeconds(updateInterval);
        }
    }
    private IEnumerator TradesUpdateLoop()
{
    Debug.Log("[TRADES] TradesUpdateLoop started");

    while (isTrading)
    {
        if (string.IsNullOrEmpty(currentConditionId))
        {
            yield return new WaitForSeconds(1f);
            continue;
        }

        bool done = false;
        DataApiTrade[] trades = null;

        yield return StartCoroutine(apiManager.GetTradesByCondition(
            currentConditionId,
            limit: 200,
            offset: 0,
            onSuccess: t => { trades = t; done = true; },
            onError: err => { Debug.LogWarning("[TRADES] error: " + err); done = true; }
        ));

        if (done && trades != null)
            UpdateVolumeFromDataTrades(trades);

        yield return new WaitForSeconds(5f);
    }
}
private void UpdateVolumeFromDataTrades(DataApiTrade[] trades)
{
    if (trades == null) return;

    DateTime sessionStart = marketState.Current.sessionStartTime;
    DateTime sessionEnd   = marketState.Current.sessionEndTime;

    double volume = 0;

    foreach (var t in trades)
    {
        // Data API timestamps are in seconds.
        DateTime ts = DateTimeOffset.FromUnixTimeSeconds(t.timestamp).UtcDateTime;

        if (ts < sessionStart || ts > sessionEnd)
            continue;

        volume += t.price * t.size;
    }

    marketState.Current.volumeThis15m = Math.Round(volume, 2);
}

    private IEnumerator GetHistoricalStartingPrice(DateTime marketStartTime)
    {
        Debug.Log($"[MarketController] Requesting BTC price at {marketStartTime:HH:mm:ss}");

        bool priceObtained = false;
        double historicalPrice = 0;

        yield return StartCoroutine(apiManager.GetPriceAtMarketStart(marketStartTime,
            onSuccess: (price) => {
                historicalPrice = price;
                priceObtained = true;
                Debug.Log($"[MarketController] Historical price: ${price:F2}");
            },
            onError: (error) => {
                Debug.Log($"[MarketController] Historical price failed: {error}");
            }
        ));

        float timeout = 8f;
        while (!priceObtained && timeout > 0)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (priceObtained)
        {
            marketState.SetPriceToBeat(historicalPrice);
            yield break;
        }

        Debug.Log("[MarketController] Using current price as fallback");
        yield return StartCoroutine(GetCurrentPriceAsFallback());
    }

    private IEnumerator GetCurrentPriceAsFallback()
    {
        bool priceObtained = false;
        double currentPrice = 0;

        yield return StartCoroutine(apiManager.GetCurrentBTCPrice(
            onSuccess: (price) => {
                currentPrice = price;
                priceObtained = true;
                Debug.Log($"[MarketController] Current price fallback: ${price:F2}");
            },
            onError: (error) => {
                Debug.Log($"[MarketController] Fallback price error: {error}");
            }
        ));

        float timeout = 3f;
        while (!priceObtained && timeout > 0)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (priceObtained)
        {
            marketState.SetPriceToBeat(currentPrice);
        }
        else
        {
            marketState.SetPriceToBeat(testStartPrice);
            Debug.Log($"[MarketController] Using test price: ${testStartPrice:F2}");
        }
    }

    private void StartDataUpdates()
    {
        if (dataUpdateCoroutine != null)
        {
            StopCoroutine(dataUpdateCoroutine);
        }

        dataUpdateCoroutine = StartCoroutine(DataUpdateLoop());
    }

    private IEnumerator DataUpdateLoop()
    {
        Debug.Log("[MarketController] Starting data updates");

        int updateCount = 0;

        while (marketState.IsMarketActive() && isTrading)
        {
            updateCount++;

            if (updateCount % 30 == 0)
            {
                int secondsRemaining = marketState.GetSecondsRemaining();
                Debug.Log($"[MarketController] Update #{updateCount}, Time remaining: {secondsRemaining / 60}:{secondsRemaining % 60:D2}");
            }

            yield return StartCoroutine(apiManager.GetCurrentBTCPrice(
                onSuccess: (price) => marketState.UpdateCurrentPrice(price),
                onError: (error) => Debug.Log($"[MarketController] Price update error: {error}")
            ));

            if (marketState.hasRealOrderBook)
            {
                marketState.UpdateFromRealOrderBook();
            }
            else
            {
                marketState.GenerateTestOrderbookData(); // random test prices when no order book is loaded
            }

            UpdateUI();

            yield return new WaitForSeconds(updateInterval);
        }

        if (isTrading && !marketState.IsMarketActive() && marketState.IsInitialized)
        {
            Debug.Log("[MarketController] Data updates stopped - market ended");
        }
    }

    private void StartMarketResetCycle()
    {
        if (marketResetCoroutine != null)
        {
            StopCoroutine(marketResetCoroutine);
        }

        marketResetCoroutine = StartCoroutine(MarketResetCycle());
    }

    private IEnumerator MarketResetCycle()
    {
        Debug.Log("[MarketController] Starting 15-minute market reset cycle");

        while (isTrading)
        {

            yield return new WaitForSeconds(900f);

            if (isTrading)
            {
                Debug.Log("[MarketController] 15-minute cycle complete - resetting market");
                currentMarketId++;

                if (dataUpdateCoroutine != null)
                {
                    StopCoroutine(dataUpdateCoroutine);
                    dataUpdateCoroutine = null;
                }

                if (marketSearchCoroutine != null)
                {
                    StopCoroutine(marketSearchCoroutine);
                }
                marketSearchCoroutine = StartCoroutine(FindAndSetupMarket());
            }
        }
    }

    private void UpdateUI()
    {
        if (UIDisplay.Instance != null)
        {
            UIDisplay.Instance.UpdateDisplay(marketState.Current);
        }
    }

    private void LogMarketData()
    {
        if (!enableDebugLogs || !marketState.IsInitialized || !isTrading) return;

        var data = marketState.Current;
        DateTime now = DateTime.Now;
        int secondsRemaining = marketState.GetSecondsRemaining();

        TimeSpan timeElapsed = DateTime.UtcNow - data.sessionStartTime;
        TimeSpan marketDuration = data.sessionEndTime - data.sessionStartTime;

        Debug.Log($"=== Market Data [{now:HH:mm:ss}] ===");
        Debug.Log($"Market: {data.eventTitle}");
        Debug.Log($"Time elapsed: {timeElapsed:mm\\:ss} / {marketDuration:mm\\:ss}");
        Debug.Log($"Remaining: {secondsRemaining / 60:D2}:{secondsRemaining % 60:D2}");
        Debug.Log($"Price To Beat: ${data.priceToBeatChainlink:F2}");
        Debug.Log($"Current Price: ${data.currentBtcPrice:F2}");
        Debug.Log($"Spread: ${data.spreadFromStart:F2} ({(data.spreadFromStart/data.priceToBeatChainlink*100):F2}%)");
        Debug.Log($"UP Bid/Ask: ${data.bidUp:F3} / ${data.askUp:F3}");
        Debug.Log($"DOWN Bid/Ask: ${data.bidDown:F3} / ${data.askDown:F3}");
        Debug.Log($"Volume: ${data.volumeThis15m:F2}");
        Debug.Log("================================");
    }

    private void OnMarketCompleted()
    {

        var data = marketState.Current;
        bool isUpWin = data.currentBtcPrice > data.priceToBeatChainlink;

        Debug.Log($"[MARKET END] {data.eventTitle} | Result: {(isUpWin ? "UP WIN" : "DOWN WIN")} | Target: ${data.priceToBeatChainlink:F2} | Final: ${data.currentBtcPrice:F2}");

        Debug.Log("=== MARKET COMPLETED ===");
        Debug.Log($"Market: {data.eventTitle}");
        Debug.Log($"Start Time: {data.sessionStartTime:HH:mm:ss}");
        Debug.Log($"End Time: {data.sessionEndTime:HH:mm:ss}");
        Debug.Log($"Price To Beat: ${data.priceToBeatChainlink:F2}");
        Debug.Log($"Final Price: ${data.currentBtcPrice:F2}");
        Debug.Log($"Spread: ${data.spreadFromStart:F2} ({(data.spreadFromStart/data.priceToBeatChainlink*100):F2}%)");
        Debug.Log($"Result: {(isUpWin ? "UP WIN" : "DOWN WIN")}");
        Debug.Log("=========================");

        if (UIDisplay.Instance != null)
        {
            UIDisplay.Instance.SetResultMessage(isUpWin ? "UP WIN" : "DOWN WIN");

        }
    }

    void OnDestroy()
    {
        StopMonitoring();
    }

    // Test-only helper.
    public void ForceNewMarketSearch()
    {
        if (isTrading)
        {
            currentMarketId++;

            if (dataUpdateCoroutine != null)
            {
                StopCoroutine(dataUpdateCoroutine);
                dataUpdateCoroutine = null;
            }

            if (marketSearchCoroutine != null)
            {
                StopCoroutine(marketSearchCoroutine);
            }
            marketSearchCoroutine = StartCoroutine(FindAndSetupMarket());
        }
    }
}