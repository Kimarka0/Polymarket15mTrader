using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using NUnit.Framework;

// Reference test: the formulas below are copied line by line from
// PolymarketTradeManager (BuyCoroutine, SellCoroutine). The calculator must give the same numbers.
public class LegacyFormulaReferenceTests
{
    // Mirrors PolymarketConstants.AMOUNT_SCALE (same type: long).
    private static class PolymarketConstants
    {
        public const long AMOUNT_SCALE = 1_000_000;
    }

    // Returns false where TradeManager calls Fail(...) or retries.
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
    private static IEnumerable<double> Prices()
    {
        for (int i = 1; i <= 999; i++)
        {
            string text = (i / 1000m).ToString(CultureInfo.InvariantCulture); // "0.001" ... "0.999"
            yield return double.Parse(text, CultureInfo.InvariantCulture);
        }
    }

    private static IEnumerable<float> UsdAmounts()
    {
        float[] fixedAmounts =
        {
            0.0000025f, 0.01f, 0.1f, 1f, 1.5f, 2.5f, 3.33f, 5f, 5.1f, 7.77f,
            10f, 12.34f, 16.03f, 19.99f, 33.33f, 100f, 123.45f, 999.99f, 1000f, 25000.5f
        };
        foreach (float usd in fixedAmounts)
            yield return usd;

        // Every cent from 0.01 to 50.00, as typed into the UI and parsed into a float.
        for (int cents = 1; cents <= 5000; cents++)
            yield return float.Parse((cents / 100m).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        var random = new Random(12345);
        for (int i = 0; i < 2000; i++)
            yield return (float)(random.NextDouble() * 10000);
    }

    [Test]
    public void Buy_MatchesTradeManagerFormulas()
    {
        int compared = 0;
        var prices = new List<double>(Prices());

        foreach (float usd in UsdAmounts())
        {
            foreach (double ask in prices)
            {
                bool legacyOk = LegacyBuy(usd, ask, out BigInteger legacyMaker, out BigInteger legacyTaker);
                bool ok = OrderAmountCalculator.TryCalculateBuy(usd, ask, out OrderAmounts amounts, out _);

                Assert.That(ok, Is.EqualTo(legacyOk), $"usd={usd:R} ask={ask:R}");
                if (!ok)
                    continue;

                Assert.That(new BigInteger(amounts.MakerAmount), Is.EqualTo(legacyMaker), $"maker: usd={usd:R} ask={ask:R}");
                Assert.That(new BigInteger(amounts.TakerAmount), Is.EqualTo(legacyTaker), $"taker: usd={usd:R} ask={ask:R}");
                compared++;
            }
        }

        TestContext.Out.WriteLine($"BUY cases compared: {compared}");
    }

    [Test]
    public void Sell_MatchesTradeManagerFormulas()
    {
        int compared = 0;
        double[] shareSizes = { 5.0, 0.5, 1.0, 3.3, 7.25, 10.0, 27.027027, 100.0, 1234.5678 };

        foreach (double shares in shareSizes)
        {
            foreach (double bid in Prices())
            {
                bool legacyOk = LegacySell(shares, bid, out BigInteger legacyMaker, out BigInteger legacyTaker);
                bool ok = OrderAmountCalculator.TryCalculateSell(shares, bid, out OrderAmounts amounts, out _);

                Assert.That(ok, Is.EqualTo(legacyOk), $"shares={shares:R} bid={bid:R}");
                if (!ok)
                    continue;

                Assert.That(new BigInteger(amounts.MakerAmount), Is.EqualTo(legacyMaker), $"maker: shares={shares:R} bid={bid:R}");
                Assert.That(new BigInteger(amounts.TakerAmount), Is.EqualTo(legacyTaker), $"taker: shares={shares:R} bid={bid:R}");
                compared++;
            }
        }

        TestContext.Out.WriteLine($"SELL cases compared: {compared}");
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Buy_WhereTradeManagerWouldThrow_CalculatorReturnsFalse(float usd)
    {
        // In TradeManager both checks are false for NaN and pass for +Infinity,
        // so new BigInteger(...) throws OverflowException inside the coroutine.
        Assert.Throws<OverflowException>(() => LegacyBuy(usd, 0.5, out _, out _));
        Assert.That(OrderAmountCalculator.TryCalculateBuy(usd, 0.5, out _, out _), Is.False);
    }
}
