using System;
using System.Collections.Generic;
using System.Globalization;

// Collects the trades of the current market between polls, without duplicates.
// One Data API page has only the latest trades, so the volume of a busy market needs all pages since the interval start.
// No UnityEngine: tested in OrderMath.
public class TradeAccumulator
{
    private readonly Dictionary<string, TradeRecord> trades = new Dictionary<string, TradeRecord>();

    public string ConditionId { get; private set; }
    public int Count => trades.Count;
    public IEnumerable<TradeRecord> Trades => trades.Values;

    // Newest trade time we already have, in unix seconds (0 when empty).
    public long NewestTimestamp { get; private set; }

    // A new market starts from an empty list; the same market keeps what was collected.
    public void ResetIfMarketChanged(string conditionId)
    {
        if (conditionId == ConditionId) return;

        trades.Clear();
        NewestTimestamp = 0;
        ConditionId = conditionId;
    }

    // Adds trades that are not in the list yet. Returns how many were new.
    public int Add(IEnumerable<TradeRecord> page)
    {
        if (page == null) return 0;

        int added = 0;
        foreach (TradeRecord trade in page)
        {
            if (string.IsNullOrEmpty(trade.Key) || trades.ContainsKey(trade.Key))
                continue;

            trades.Add(trade.Key, trade);
            if (trade.TimestampSeconds > NewestTimestamp) NewestTimestamp = trade.TimestampSeconds;
            added++;
        }

        return added;
    }

    // One fill is identified by the transaction and everything that can differ inside it.
    public static string MakeKey(string transactionHash, string asset, string side, string wallet, double size, double price, long timestamp)
    {
        return string.Join("|",
            transactionHash, asset, side, wallet,
            size.ToString("R", CultureInfo.InvariantCulture),
            price.ToString("R", CultureInfo.InvariantCulture),
            timestamp.ToString(CultureInfo.InvariantCulture));
    }

    // Pages come newest first. Keep paging while the page is full, still inside the interval
    // and not yet back at trades we had before this poll. maxPages limits one poll.
    public static bool ShouldFetchNextPage(int pageSize, int limit, long oldestTimestampInPage,
                                           long newestKnownBeforePoll, DateTime intervalStart,
                                           int pagesFetched, int maxPages)
    {
        if (pagesFetched >= maxPages) return false;
        if (pageSize < limit) return false;

        // Same second as our newest trade: there may be more trades with that timestamp on the next page.
        if (newestKnownBeforePoll > 0 && oldestTimestampInPage < newestKnownBeforePoll) return false;

        DateTime oldest = DateTimeOffset.FromUnixTimeSeconds(oldestTimestampInPage).UtcDateTime;
        return oldest >= intervalStart;
    }
}
