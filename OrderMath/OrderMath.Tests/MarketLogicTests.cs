using System;
using NUnit.Framework;

public class MarketIntervalTests
{
    private static DateTime Utc(int hour, int minute, int second, int millisecond = 0)
    {
        return new DateTime(2026, 10, 12, hour, minute, second, millisecond, DateTimeKind.Utc);
    }

    [Test]
    public void GetStart_LastMillisecondOfInterval_RoundsDown()
    {
        Assert.That(MarketInterval.GetStart(Utc(12, 14, 59, 999)), Is.EqualTo(Utc(12, 0, 0)));
    }

    [Test]
    public void GetStart_ExactBoundary_StartsNewInterval()
    {
        Assert.That(MarketInterval.GetStart(Utc(12, 15, 0)), Is.EqualTo(Utc(12, 15, 0)));
    }

    [Test]
    public void GetStart_LastIntervalOfTheDay()
    {
        Assert.That(MarketInterval.GetStart(Utc(23, 59, 59, 500)), Is.EqualTo(Utc(23, 45, 0)));
    }

    [Test]
    public void GetStart_KeepsTicksBelowOneMillisecond_LikeTheOriginalCode()
    {
        // Known quirk kept on purpose: 100-ns ticks are not removed.
        DateTime time = Utc(12, 7, 30, 250).AddTicks(1234);
        Assert.That(MarketInterval.GetStart(time), Is.EqualTo(Utc(12, 0, 0).AddTicks(1234)));
    }

    [Test]
    public void GetEnd_IsFifteenMinutesAfterStart()
    {
        Assert.That(MarketInterval.GetEnd(Utc(12, 0, 0)), Is.EqualTo(Utc(12, 15, 0)));
    }

    [Test]
    public void GetSlugTimestamp_IsUnixSecondsOfIntervalStart()
    {
        // 2026-10-12 12:00:00 UTC = 1791806400 (calculated separately).
        Assert.That(MarketInterval.GetSlugTimestamp(Utc(12, 14, 59)), Is.EqualTo("1791806400"));
        Assert.That(MarketInterval.GetSlugTimestamp(Utc(12, 15, 0)), Is.EqualTo("1791807300"));
    }

    [Test]
    public void GetSlugCandidates_TriesKnownFormatsInOrder()
    {
        Assert.That(MarketInterval.GetSlugCandidates("1791806400"), Is.EqualTo(new[]
        {
            "btc-updown-15m-1791806400",
            "bitcoin-updown-15m-1791806400",
            "btc-15m-1791806400",
            "bitcoin-15m-1791806400"
        }));
    }
}

public class TradeVolumeCalculatorTests
{
    private static readonly DateTime Start = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddMinutes(15);
    private const long StartSeconds = 1791806400;

    private static TradeRecord Trade(long secondsFromStart, double price, double size)
    {
        return new TradeRecord { TimestampSeconds = StartSeconds + secondsFromStart, Price = price, Size = size };
    }

    [Test]
    public void SumsPriceTimesSizeInsideTheInterval()
    {
        var trades = new[] { Trade(10, 0.5, 10), Trade(600, 0.25, 4) };
        Assert.That(TradeVolumeCalculator.SumInInterval(trades, Start, End), Is.EqualTo(6.0));
    }

    [Test]
    public void IncludesBothEndsOfTheInterval()
    {
        var trades = new[] { Trade(0, 1.0, 1), Trade(900, 1.0, 2) };
        Assert.That(TradeVolumeCalculator.SumInInterval(trades, Start, End), Is.EqualTo(3.0));
    }

    [Test]
    public void IgnoresTradesOutsideTheInterval()
    {
        var trades = new[] { Trade(-1, 1.0, 100), Trade(901, 1.0, 100), Trade(5, 0.5, 2) };
        Assert.That(TradeVolumeCalculator.SumInInterval(trades, Start, End), Is.EqualTo(1.0));
    }

    [Test]
    public void RoundsToCents()
    {
        var trades = new[] { Trade(1, 0.333, 1) };
        Assert.That(TradeVolumeCalculator.SumInInterval(trades, Start, End), Is.EqualTo(0.33));
    }

    [Test]
    public void EmptyOrNullList_IsZero()
    {
        Assert.That(TradeVolumeCalculator.SumInInterval(new TradeRecord[0], Start, End), Is.EqualTo(0.0));
        Assert.That(TradeVolumeCalculator.SumInInterval(null, Start, End), Is.EqualTo(0.0));
    }
}
