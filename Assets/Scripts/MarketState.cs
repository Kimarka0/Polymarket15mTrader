using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

public class MarketState
{
    private ActiveMarketData current;
    private bool isInitialized;
    private System.Random random = new System.Random();
    public string upTokenId;
    public string downTokenId;
    public string conditionId;

    public ClobOrderBook upOrderBook;
    public ClobOrderBook downOrderBook;

    public bool hasRealOrderBook =>
        upOrderBook != null && downOrderBook != null;

    public ActiveMarketData Current => current;
    public bool IsInitialized => isInitialized;

    public MarketState()
    {
        current = new ActiveMarketData();
        current.lastUpdateTime = DateTime.UtcNow;
        isInitialized = false;
    }
        public void UpdateOrderBooks(
        ClobOrderBook up,
        ClobOrderBook down
    )
    {
        upOrderBook = up;
        downOrderBook = down;
    }
    public void UpdateFromRealOrderBook()
    {
        try
        {
            if (upOrderBook == null || downOrderBook == null)
                return;

            // Prices come as strings like "0.53"; InvariantCulture keeps '.' as the decimal separator on any OS locale.
            if (upOrderBook.bids != null && upOrderBook.bids.Length > 0)
            {
                current.bidUp = upOrderBook.bids
                    .Select(b => double.Parse(b.price, CultureInfo.InvariantCulture))
                    .Max();
            }

            if (upOrderBook.asks != null && upOrderBook.asks.Length > 0)
            {
                current.askUp = upOrderBook.asks
                    .Select(a => double.Parse(a.price, CultureInfo.InvariantCulture))
                    .Min();
            }

            if (downOrderBook.bids != null && downOrderBook.bids.Length > 0)
            {
                current.bidDown = downOrderBook.bids
                    .Select(b => double.Parse(b.price, CultureInfo.InvariantCulture))
                    .Max();
            }

            if (downOrderBook.asks != null && downOrderBook.asks.Length > 0)
            {
                current.askDown = downOrderBook.asks
                    .Select(a => double.Parse(a.price, CultureInfo.InvariantCulture))
                    .Min();
            }

            current.lastUpdateTime = DateTime.UtcNow;
        }
        catch (Exception e)
        {
            Debug.LogError("[MarketState] UpdateFromRealOrderBook error: " + e.Message);
        }
    }

    public void SetInitialized(bool value)
    {
        isInitialized = value;
    }

    public void UpdateMarketInfo(PolymarketEvent market)
    {
        try
        {
            current.eventId = market.id ?? "unknown";
            current.eventTitle = GetMarketDisplayName(market);

            // API start/end dates are unreliable, so the interval is computed from the current UTC time.

            DateTime now = DateTime.UtcNow;
            DateTime marketStartTime = Calculate15MinuteStartTime(now);
            DateTime marketEndTime = MarketInterval.GetEnd(marketStartTime);

            current.sessionStartTime = marketStartTime;
            current.sessionEndTime = marketEndTime;
            current.lastUpdateTime = now;

            Debug.Log($"[MarketState] Market: {current.eventTitle}");
            Debug.Log($"[MarketState] Calculated interval: {marketStartTime:HH:mm:ss} - {marketEndTime:HH:mm:ss}");
            Debug.Log($"[MarketState] Current UTC: {now:HH:mm:ss}");
            Debug.Log($"[MarketState] Duration: 15.0 minutes");

            isInitialized = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[MarketState] UpdateMarketInfo error: {e.Message}");

            DateTime now = DateTime.UtcNow;
            DateTime startTime = Calculate15MinuteStartTime(now);

            current.sessionStartTime = startTime;
            current.sessionEndTime = MarketInterval.GetEnd(startTime);
            current.lastUpdateTime = now;
            isInitialized = true;
        }
    }

    private DateTime Calculate15MinuteStartTime(DateTime currentTime)
    {
        return MarketInterval.GetStart(currentTime);
    }

    public void SetPriceToBeat(double price)
    {
        current.priceToBeatChainlink = price;
        current.currentBtcPrice = price;
        current.spreadFromStart = 0;
        Debug.Log($"[MarketState] Target price set: ${price:F2}");
    }

    public void UpdateCurrentPrice(double price)
    {
        current.currentBtcPrice = price;
        current.spreadFromStart = price - current.priceToBeatChainlink;
        current.lastUpdateTime = DateTime.UtcNow;
    }

    public void GenerateTestOrderbookData()
    {
        try
        {
            double spread = 0.01 + (random.NextDouble() * 0.02);

            current.bidUp = Math.Round(0.5 - spread/2, 3);
            current.askUp = Math.Round(0.5 + spread/2, 3);

            current.bidDown = Math.Round(1 - current.askUp, 3);
            current.askDown = Math.Round(1 - current.bidUp, 3);

            current.lastUpdateTime = DateTime.UtcNow;
        }
        catch (Exception e)
        {
            Debug.LogError($"[MarketState] GenerateTestOrderbookData error: {e.Message}");
        }
    }

    public void UpdateVolumeFromOrderBooks()
{
    try
    {
        double volume = 0;

        if (upOrderBook != null)
        {
            volume += SumOrderBookValue(upOrderBook);
        }

        if (downOrderBook != null)
        {
            volume += SumOrderBookValue(downOrderBook);
        }

        current.volumeThis15m = Math.Round(volume, 2);
    }
    catch (Exception e)
    {
        Debug.LogError("[MarketState] UpdateVolumeFromOrderBooks error: " + e.Message);
    }
}

    private double SumOrderBookValue(ClobOrderBook book, int depth = 10)
    {
        double sum = 0;

        if (book == null) return 0;

        for (int i = 0; i < Mathf.Min(depth, book.bids?.Length ?? 0); i++)
        {
            double price = double.Parse(book.bids[i].price, CultureInfo.InvariantCulture);
            double size  = double.Parse(book.bids[i].size, CultureInfo.InvariantCulture);
            sum += price * size;
        }

        for (int i = 0; i < Mathf.Min(depth, book.asks?.Length ?? 0); i++)
        {
            double price = double.Parse(book.asks[i].price, CultureInfo.InvariantCulture);
            double size  = double.Parse(book.asks[i].size, CultureInfo.InvariantCulture);
            sum += price * size;
        }

        return sum;
    }
   public void UpdateVolumeFromTrades(
    ClobTrade[] upTrades,
    ClobTrade[] downTrades
)
{
    double volume = 0;

    DateTime start = current.sessionStartTime;
    DateTime end   = current.sessionEndTime;

    void Accumulate(ClobTrade[] trades)
    {
        if (trades == null) return;

        foreach (var t in trades)
        {
            DateTime ts = DateTimeOffset
                .FromUnixTimeMilliseconds(t.timestamp)
                .UtcDateTime;

            if (ts < start || ts > end)
                continue;

            double price = double.Parse(t.price, CultureInfo.InvariantCulture);
            double size  = double.Parse(t.size,  CultureInfo.InvariantCulture);

            volume += price * size;
        }
    }

    Accumulate(upTrades);
    Accumulate(downTrades);

    current.volumeThis15m = volume;
}

    public int GetSecondsRemaining()
    {
        try
        {
            TimeSpan remaining = current.sessionEndTime - DateTime.UtcNow;
            return (int)Math.Max(0, remaining.TotalSeconds);
        }
        catch
        {
            return 0;
        }
    }

    public bool IsMarketActive()
    {
        if (!isInitialized) return false;

        DateTime now = DateTime.UtcNow;
        TimeSpan timeSinceStart = now - current.sessionStartTime;

        // 15.5 min allows a 30 s buffer, but GetSecondsRemaining() > 0 still ends it at 15 min.
        bool isActive = timeSinceStart.TotalMinutes < 15.5 && GetSecondsRemaining() > 0;

        if (timeSinceStart.TotalMinutes >= 16)
        {
            return false;
        }

        return isActive;
    }

    private string GetMarketDisplayName(PolymarketEvent market)
    {
        if (!string.IsNullOrEmpty(market.title))
            return market.title;
        if (!string.IsNullOrEmpty(market.ticker))
            return market.ticker;
        if (!string.IsNullOrEmpty(market.slug))
            return market.slug;
        return "Bitcoin Up/Down [15min]";
    }
}