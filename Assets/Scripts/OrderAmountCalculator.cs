using System;

// Order amounts in micro units: 1 USDC = 1 share = 1_000_000.
public struct OrderAmounts
{
    public long MakerAmount;
    public long TakerAmount;
}

// All money math is done in decimal. Inputs that come as float/double from Unity UI or the order book
// are converted once at the start, so values like $16.03 stay exact.
public static class OrderAmountCalculator
{
    public const long AmountScale = 1_000_000;

    public const decimal MinValue = 0.000001m;

    // Amounts above this do not fit safely into long (9e18 micro = 9e12 USD).
    public const decimal MaxMicroAmount = 9e18m;

    // Smallest share step used when selling a position.
    public const decimal ShareStep = 0.01m;

    // BUY: maker pays USDC, taker receives shares.
    public static bool TryCalculateBuy(float usdToSpend, double askPrice, out OrderAmounts amounts, out string error)
    {
        if (!TryToDecimal(usdToSpend, out decimal usd) || !TryToDecimal(askPrice, out decimal ask))
        {
            amounts = default;
            error = "Amount and price must be finite numbers.";
            return false;
        }

        return TryCalculateBuy(usd, ask, out amounts, out error);
    }

    public static bool TryCalculateBuy(decimal usdToSpend, decimal askPrice, out OrderAmounts amounts, out string error)
    {
        amounts = default;

        if (askPrice <= MinValue)
        {
            error = "Ask price is invalid.";
            return false;
        }

        decimal size = usdToSpend / askPrice;
        if (size <= MinValue)
        {
            error = "Size is too small.";
            return false;
        }

        decimal makerMicro, takerMicro;
        try
        {
            makerMicro = usdToSpend * AmountScale;
            takerMicro = size * AmountScale;
        }
        catch (OverflowException)
        {
            error = "Amount is too large.";
            return false;
        }

        return TryBuild(makerMicro, takerMicro, out amounts, out error);
    }

    // SELL: maker gives shares, taker pays USDC.
    public static bool TryCalculateSell(decimal sharesToSell, double bidPrice, out OrderAmounts amounts, out string error)
    {
        if (!TryToDecimal(bidPrice, out decimal bid))
        {
            amounts = default;
            error = "Price must be a finite number.";
            return false;
        }

        return TryCalculateSell(sharesToSell, bid, out amounts, out error);
    }

    public static bool TryCalculateSell(decimal sharesToSell, decimal bidPrice, out OrderAmounts amounts, out string error)
    {
        amounts = default;

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

        decimal makerMicro, takerMicro;
        try
        {
            makerMicro = sharesToSell * AmountScale;
            takerMicro = sharesToSell * bidPrice * AmountScale;
        }
        catch (OverflowException)
        {
            error = "Amount is too large.";
            return false;
        }

        return TryBuild(makerMicro, takerMicro, out amounts, out error);
    }

    // Position sizes from the Data API can have many decimals; never sell more than we hold.
    public static decimal RoundDownShares(decimal shares)
    {
        if (shares <= 0m) return 0m;
        return Math.Floor(shares / ShareStep) * ShareStep;
    }

    // (decimal)float keeps 7 significant digits, so 16.03f becomes exactly 16.03.
    public static bool TryToDecimal(float value, out decimal result)
    {
        result = 0m;
        if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 1e15f) return false;
        result = (decimal)value;
        return true;
    }

    public static bool TryToDecimal(double value, out decimal result)
    {
        result = 0m;
        if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > 1e15) return false;
        result = (decimal)value;
        return true;
    }

    private static bool TryBuild(decimal makerMicro, decimal takerMicro, out OrderAmounts amounts, out string error)
    {
        amounts = default;

        // Math.Round(decimal) uses banker's rounding, the same as the original double code.
        makerMicro = Math.Round(makerMicro);
        takerMicro = Math.Round(takerMicro);

        if (makerMicro > MaxMicroAmount || takerMicro > MaxMicroAmount)
        {
            error = "Amount is too large.";
            return false;
        }

        amounts.MakerAmount = (long)makerMicro;
        amounts.TakerAmount = (long)takerMicro;
        error = null;
        return true;
    }
}
