using System;

// 15-minute market interval and the slugs Polymarket uses for it. No UnityEngine: tested in OrderMath.
public static class MarketInterval
{
    public const int LengthMinutes = 15;
    private const long LengthSeconds = LengthMinutes * 60;

    // Rounds down to the start of the 15-minute interval (12:14:59 -> 12:00:00).
    // Ticks below one millisecond are kept, as in the original MarketState code.
    public static DateTime GetStart(DateTime time)
    {
        int remainder = time.Minute % LengthMinutes;
        return time.AddMinutes(-remainder)
                   .AddSeconds(-time.Second)
                   .AddMilliseconds(-time.Millisecond);
    }

    public static DateTime GetEnd(DateTime start)
    {
        return start.AddMinutes(LengthMinutes);
    }

    // Unix seconds of the interval start, as used in market slugs, e.g. "btc-updown-15m-1791806400".
    public static string GetSlugTimestamp(DateTime utcNow)
    {
        long unixSeconds = ((DateTimeOffset)utcNow).ToUnixTimeSeconds();
        return (unixSeconds / LengthSeconds * LengthSeconds).ToString();
    }

    // Slug formats to try, in order.
    public static string[] GetSlugCandidates(string slugTimestamp)
    {
        return new[]
        {
            $"btc-updown-15m-{slugTimestamp}",
            $"bitcoin-updown-15m-{slugTimestamp}",
            $"btc-15m-{slugTimestamp}",
            $"bitcoin-15m-{slugTimestamp}"
        };
    }
}
