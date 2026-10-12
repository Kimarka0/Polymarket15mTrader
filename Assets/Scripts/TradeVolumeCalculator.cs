using System;
using System.Collections.Generic;

// The fields of a Data API trade that the volume needs.
public struct TradeRecord
{
    public long TimestampSeconds;
    public double Price;
    public double Size;
}

// USD volume of trades inside one market interval. No UnityEngine: tested in OrderMath.
public static class TradeVolumeCalculator
{
    // Both ends of the interval are included. Result is rounded to cents.
    public static double SumInInterval(IEnumerable<TradeRecord> trades, DateTime start, DateTime end)
    {
        if (trades == null) return 0;

        double volume = 0;
        foreach (TradeRecord trade in trades)
        {
            // Data API timestamps are in seconds.
            DateTime time = DateTimeOffset.FromUnixTimeSeconds(trade.TimestampSeconds).UtcDateTime;
            if (time < start || time > end)
                continue;

            volume += trade.Price * trade.Size;
        }

        return Math.Round(volume, 2);
    }
}
