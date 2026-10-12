using System;

// Order amounts in micro units: 1 USDC = 1 share = 1_000_000.
public struct OrderAmounts
{
    public long MakerAmount;
    public long TakerAmount;
}

public static class OrderAmountCalculator
{

    public const long AmountScale = 1_000_000;

    public const double MinValue = 0.000001;

    public const double MaxMicroAmount = 9e18;

    public static bool TryCalculateBuy(float usdToSpend, double askPrice, out OrderAmounts amounts, out string error)
    {
        amounts = default;

        if (!IsFinite(usdToSpend) || !IsFinite(askPrice))
        {
            error = "Amount and price must be finite numbers.";
            return false;
        }

        if (askPrice <= MinValue)
        {
            error = "Ask price is invalid.";
            return false;
        }

        double size = usdToSpend / askPrice;
        if (size <= MinValue)
        {
            error = "Size is too small.";
            return false;
        }

        double makerMicro = Math.Round(usdToSpend * AmountScale);
        double takerMicro = Math.Round(size * AmountScale);

        return TryBuild(makerMicro, takerMicro, out amounts, out error);
    }

    public static bool TryCalculateSell(double sharesToSell, double bidPrice, out OrderAmounts amounts, out string error)
    {
        amounts = default;

        if (!IsFinite(sharesToSell) || !IsFinite(bidPrice))
        {
            error = "Size and price must be finite numbers.";
            return false;
        }

        if (bidPrice <= MinValue)
        {
            error = "Bid price is invalid.";
            return false;
        }

        if (sharesToSell <= MinValue)
        {
            error = "Size is too small.";
            return false;
        }

        double makerMicro = Math.Round(sharesToSell * AmountScale);
        double takerMicro = Math.Round(sharesToSell * bidPrice * AmountScale);

        return TryBuild(makerMicro, takerMicro, out amounts, out error);
    }

    private static bool TryBuild(double makerMicro, double takerMicro, out OrderAmounts amounts, out string error)
    {
        amounts = default;

        if (!IsInRange(makerMicro) || !IsInRange(takerMicro))
        {
            error = "Amount is too large.";
            return false;
        }

        amounts.MakerAmount = (long)makerMicro;
        amounts.TakerAmount = (long)takerMicro;
        error = null;
        return true;
    }

    private static bool IsInRange(double micro)
    {
        return IsFinite(micro) && micro <= MaxMicroAmount;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
