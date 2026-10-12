using System;
using System.Linq;
using NUnit.Framework;

public class TradeAccumulatorTests
{
    private static readonly DateTime Start = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddMinutes(15);
    private const long StartSeconds = 1791806400; // 2026-10-12 12:00:00 UTC

    private static TradeRecord Trade(string tx, long secondsFromStart, double price = 0.5, double size = 10)
    {
        long ts = StartSeconds + secondsFromStart;
        return new TradeRecord
        {
            Key = TradeAccumulator.MakeKey(tx, "token", "BUY", "0xwallet", size, price, ts),
            TimestampSeconds = ts,
            Price = price,
            Size = size
        };
    }

    [Test]
    public void Add_SkipsTradesAlreadyCollected()
    {
        var acc = new TradeAccumulator();
        acc.ResetIfMarketChanged("market-1");

        Assert.That(acc.Add(new[] { Trade("a", 10), Trade("b", 20) }), Is.EqualTo(2));
        // The next poll returns an overlapping page: only "c" is new.
        Assert.That(acc.Add(new[] { Trade("c", 30), Trade("b", 20), Trade("a", 10) }), Is.EqualTo(1));
        Assert.That(acc.Count, Is.EqualTo(3));
        Assert.That(acc.NewestTimestamp, Is.EqualTo(StartSeconds + 30));
    }

    [Test]
    public void VolumeAcrossPolls_CountsEveryTradeOnce()
    {
        var acc = new TradeAccumulator();
        acc.ResetIfMarketChanged("market-1");
        acc.Add(new[] { Trade("a", 10), Trade("b", 20) });
        acc.Add(new[] { Trade("c", 30), Trade("b", 20) });

        // 3 trades x 0.5 x 10 = 15 USD; a single page approach would only see the last page.
        Assert.That(TradeVolumeCalculator.SumInInterval(acc.Trades, Start, End), Is.EqualTo(15.0));
    }

    [Test]
    public void ResetIfMarketChanged_ClearsOnlyForANewMarket()
    {
        var acc = new TradeAccumulator();
        acc.ResetIfMarketChanged("market-1");
        acc.Add(new[] { Trade("a", 10) });

        acc.ResetIfMarketChanged("market-1");
        Assert.That(acc.Count, Is.EqualTo(1));

        acc.ResetIfMarketChanged("market-2");
        Assert.That(acc.Count, Is.EqualTo(0));
        Assert.That(acc.NewestTimestamp, Is.EqualTo(0));
        Assert.That(acc.ConditionId, Is.EqualTo("market-2"));
    }

    [Test]
    public void Add_IgnoresTradesWithoutKey()
    {
        var acc = new TradeAccumulator();
        Assert.That(acc.Add(new[] { new TradeRecord { TimestampSeconds = StartSeconds } }), Is.EqualTo(0));
        Assert.That(acc.Add(null), Is.EqualTo(0));
    }

    [Test]
    public void MakeKey_DiffersForDifferentFillsInOneTransaction()
    {
        string a = TradeAccumulator.MakeKey("0xtx", "up", "BUY", "0xw", 10, 0.5, StartSeconds);
        string b = TradeAccumulator.MakeKey("0xtx", "up", "BUY", "0xw", 12, 0.5, StartSeconds);
        string c = TradeAccumulator.MakeKey("0xtx", "down", "BUY", "0xw", 10, 0.5, StartSeconds);
        Assert.That(new[] { a, b, c }.Distinct().Count(), Is.EqualTo(3));
        Assert.That(TradeAccumulator.MakeKey("0xtx", "up", "BUY", "0xw", 10, 0.5, StartSeconds), Is.EqualTo(a));
    }

    // ShouldFetchNextPage(pageSize, limit, oldestInPage, newestKnownBeforePoll, intervalStart, pagesFetched, maxPages)

    [Test]
    public void NextPage_FirstPoll_FullPageInsideInterval_Continues()
    {
        Assert.That(TradeAccumulator.ShouldFetchNextPage(200, 200, StartSeconds + 300, 0, Start, 1, 25), Is.True);
    }

    [Test]
    public void NextPage_ShortPage_Stops()
    {
        Assert.That(TradeAccumulator.ShouldFetchNextPage(150, 200, StartSeconds + 300, 0, Start, 1, 25), Is.False);
    }

    [Test]
    public void NextPage_ReachedTradesBeforeIntervalStart_Stops()
    {
        Assert.That(TradeAccumulator.ShouldFetchNextPage(200, 200, StartSeconds - 1, 0, Start, 1, 25), Is.False);
    }

    [Test]
    public void NextPage_ReachedTradesKnownFromEarlierPolls_Stops()
    {
        Assert.That(TradeAccumulator.ShouldFetchNextPage(200, 200, StartSeconds + 100, StartSeconds + 120, Start, 1, 25), Is.False);
    }

    [Test]
    public void NextPage_SameSecondAsNewestKnown_Continues()
    {
        // More trades with that timestamp may be on the next page.
        Assert.That(TradeAccumulator.ShouldFetchNextPage(200, 200, StartSeconds + 120, StartSeconds + 120, Start, 1, 25), Is.True);
    }

    [Test]
    public void NextPage_PageLimitReached_Stops()
    {
        Assert.That(TradeAccumulator.ShouldFetchNextPage(200, 200, StartSeconds + 300, 0, Start, 25, 25), Is.False);
    }
}
