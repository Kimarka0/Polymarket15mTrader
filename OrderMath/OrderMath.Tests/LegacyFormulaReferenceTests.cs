using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using NUnit.Framework;

// Compares the decimal calculator with the old float/double formulas that PolymarketTradeManager
// used before. They are kept here line by line, so the size of the change is measured, not guessed.
public class LegacyFormulaReferenceTests
{
    // Mirrors PolymarketConstants.AMOUNT_SCALE (same type: long).
    private static class PolymarketConstants
    {
        public const long AMOUNT_SCALE = 1_000_000;
    }

    // Returns false where TradeManager called Fail(...) or retried.
    // Exceptions (NaN/Infinity reaching new BigInteger) are left to the caller.
    private static bool LegacyBuy(float usdToSpend, double ask, out BigInteger makerAmount, out BigInteger takerAmount)
    {
        makerAmount = BigInteger.Zero;
        takerAmount = BigInteger.Zero;

        if (ask <= 0.000001)
            return false;

        double size = usdToSpend / ask;
        if (size <= 0.000001)
            return false;

        makerAmount = new BigInteger(Math.Round(usdToSpend * PolymarketConstants.AMOUNT_SCALE)); // USDC micro
        takerAmount = new BigInteger(Math.Round(size * PolymarketConstants.AMOUNT_SCALE));       // shares micro
        return true;
    }

    private static bool LegacySell(double sharesToSell, double bid, out BigInteger makerAmount, out BigInteger takerAmount)
    {
        makerAmount = BigInteger.Zero;
        takerAmount = BigInteger.Zero;

        if (bid <= 0.000001)
            return false;

        makerAmount = new BigInteger(Math.Round(sharesToSell * PolymarketConstants.AMOUNT_SCALE)); // shares
        takerAmount = new BigInteger(Math.Round(sharesToSell * bid * PolymarketConstants.AMOUNT_SCALE)); // USDC
        return true;
    }

    // Prices as TradeManager gets them: double.Parse of the order book string.
    private static List<double> Prices()
    {
        var prices = new List<double>();
        for (int i = 1; i <= 999; i++)
        {
            string text = (i / 1000m).ToString(CultureInfo.InvariantCulture); // "0.001" ... "0.999"
            prices.Add(double.Parse(text, CultureInfo.InvariantCulture));
        }
        return prices;
    }

    // Every cent up to the 100 USD order limit, parsed from text into a float as TradeTestOnGui does.
    private static float UsdFromCents(int cents)
    {
        return float.Parse((cents / 100m).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    [Test]
    public void Buy_MakerAmountIsExactForEveryCentUpToTheLimit()
    {
        for (int cents = 1; cents <= 10_000; cents++)
        {
            Assert.That(OrderAmountCalculator.TryCalculateBuy(UsdFromCents(cents), 0.5, out OrderAmounts amounts, out _), Is.True);
            Assert.That(amounts.MakerAmount, Is.EqualTo(cents * 10_000L), $"cents={cents}");
            Assert.That(amounts.TakerAmount, Is.EqualTo(cents * 20_000L), $"cents={cents}");
        }
    }

    [Test]
    public void Buy_DiffersFromOldFloatFormulaOnlyByFloatRounding()
    {
        int compared = 0, makerDiffs = 0, takerDiffs = 0;
        BigInteger maxMakerDiff = 0, maxTakerDiff = 0;
        var examples = new List<string>();
        List<double> prices = Prices();

        for (int cents = 1; cents <= 10_000; cents++)
        {
            float usd = UsdFromCents(cents);
            foreach (double ask in prices)
            {
                bool legacyOk = LegacyBuy(usd, ask, out BigInteger oldMaker, out BigInteger oldTaker);
                bool ok = OrderAmountCalculator.TryCalculateBuy(usd, ask, out OrderAmounts amounts, out _);
                Assert.That(ok, Is.EqualTo(legacyOk), $"usd={usd:R} ask={ask:R}");
                if (!ok) continue;

                BigInteger makerDiff = BigInteger.Abs(amounts.MakerAmount - oldMaker);
                BigInteger takerDiff = BigInteger.Abs(amounts.TakerAmount - oldTaker);

                // A float has 24 bits of precision: below 2^24 micro-USDC (about $16.77) the old
                // maker amount is off by at most 1; up to $100 by at most 4 (half a float step at 1e8).
                Assert.That(makerDiff, Is.LessThanOrEqualTo(new BigInteger(4)), $"maker: usd={usd:R} ask={ask:R}");
                if (cents * 10_000L < (1L << 24))
                    Assert.That(makerDiff, Is.LessThanOrEqualTo(BigInteger.One), $"maker: usd={usd:R}");
                // The share amount comes from the same float, so its relative error is the same (< 1e-7).
                Assert.That((double)takerDiff, Is.LessThanOrEqualTo(1 + (double)oldTaker * 1e-7), $"taker: usd={usd:R} ask={ask:R}");

                if (makerDiff > 0)
                {
                    makerDiffs++;
                    string example = $"${(cents / 100m).ToString(CultureInfo.InvariantCulture)}: {oldMaker} -> {amounts.MakerAmount}";
                    if (examples.Count < 5 && !examples.Contains(example)) examples.Add(example);
                }
                if (takerDiff > 0) takerDiffs++;
                if (makerDiff > maxMakerDiff) maxMakerDiff = makerDiff;
                if (takerDiff > maxTakerDiff) maxTakerDiff = takerDiff;
                compared++;
            }
        }

        TestContext.Out.WriteLine($"BUY compared: {compared}; makerAmount changed in {makerDiffs} (max {maxMakerDiff} micro), " +
                                  $"takerAmount changed in {takerDiffs} (max {maxTakerDiff} micro).");
        TestContext.Out.WriteLine("Examples (old -> new makerAmount): " + string.Join("; ", examples));
    }

    [Test]
    public void Sell_DiffersFromOldDoubleFormulaByAtMostOneMicroUnit()
    {
        int compared = 0, diffs = 0;
        string[] shareSizes = { "5", "0.5", "1", "3.3", "7.25", "10", "27.02", "100", "1234.56" };

        foreach (string text in shareSizes)
        {
            decimal shares = decimal.Parse(text, CultureInfo.InvariantCulture);
            foreach (double bid in Prices())
            {
                bool legacyOk = LegacySell((double)shares, bid, out BigInteger oldMaker, out BigInteger oldTaker);
                bool ok = OrderAmountCalculator.TryCalculateSell(shares, bid, out OrderAmounts amounts, out _);
                Assert.That(ok, Is.EqualTo(legacyOk), $"shares={shares} bid={bid:R}");
                if (!ok) continue;

                Assert.That(new BigInteger(amounts.MakerAmount), Is.EqualTo(oldMaker), $"maker: shares={shares}");
                BigInteger takerDiff = BigInteger.Abs(amounts.TakerAmount - oldTaker);
                Assert.That(takerDiff, Is.LessThanOrEqualTo(BigInteger.One), $"taker: shares={shares} bid={bid:R}");
                if (takerDiff > 0) diffs++;
                compared++;
            }
        }

        TestContext.Out.WriteLine($"SELL compared: {compared}; takerAmount changed by 1 micro in {diffs}.");
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Buy_WhereOldCodeThrew_CalculatorReturnsFalse(float usd)
    {
        // In the old code both checks were false for NaN and passed for +Infinity,
        // so new BigInteger(...) threw OverflowException inside the coroutine.
        Assert.Throws<OverflowException>(() => LegacyBuy(usd, 0.5, out _, out _));
        Assert.That(OrderAmountCalculator.TryCalculateBuy(usd, 0.5, out _, out _), Is.False);
    }
}
