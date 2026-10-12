using System;
using System.Collections.Generic;
using System.Globalization;

// Decides whether an order book can be used to price an order. No UnityEngine: tested in OrderMath.
public static class OrderBookCheck
{
    // Books are polled every second; anything older means polling stopped or the market just switched.
    public const double MaxAgeSeconds = 5;

    // Best price of one side: the lowest ask for a buy, the highest bid for a sell.
    // The book must belong to tokenId and be fresh, otherwise reason explains why it cannot be used.
    public static bool TryGetBestPrice(string tokenId, string bookAssetId, IEnumerable<string> prices, bool lowest,
                                       DateTime loadedAtUtc, DateTime nowUtc, out decimal price, out string reason)
    {
        price = 0m;

        if (string.IsNullOrEmpty(tokenId) || loadedAtUtc == default)
        {
            reason = "the order book for this outcome is not loaded yet";
            return false;
        }

        if (string.IsNullOrEmpty(bookAssetId))
        {
            reason = "the order book has no asset_id, so it cannot be matched to this outcome";
            return false;
        }

        if (bookAssetId != tokenId)
        {
            reason = "the order book belongs to another token (the market has just switched)";
            return false;
        }

        double ageSeconds = (nowUtc - loadedAtUtc).TotalSeconds;
        if (ageSeconds > MaxAgeSeconds)
        {
            reason = $"the order book is {Math.Floor(ageSeconds).ToString(CultureInfo.InvariantCulture)} s old (limit {MaxAgeSeconds.ToString(CultureInfo.InvariantCulture)} s)";
            return false;
        }

        bool found = false;
        foreach (string text in prices ?? Array.Empty<string>())
        {
            // Prices come as strings like "0.53"; InvariantCulture keeps '.' as the decimal separator on any OS locale.
            if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal p) || p <= 0m)
                continue;

            if (!found || (lowest ? p < price : p > price))
                price = p;
            found = true;
        }

        if (!found)
        {
            reason = lowest ? "the order book has no asks" : "the order book has no bids";
            return false;
        }

        reason = null;
        return true;
    }
}
