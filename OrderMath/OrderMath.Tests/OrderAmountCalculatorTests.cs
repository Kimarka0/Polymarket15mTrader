using System;
using NUnit.Framework;

// Expected values were calculated independently (Python, float32 emulated with struct),
// not taken from the output of OrderAmountCalculator.
public class OrderAmountCalculatorTests
{

    [Test]
    public void Buy_TypicalAmount_ReturnsMicroUnits()
    {
        Assert.That(OrderAmountCalculator.TryCalculateBuy(5f, 0.5, out var amounts, out _), Is.True);
        Assert.That(amounts.MakerAmount, Is.EqualTo(5_000_000));   // 5 USDC
        Assert.That(amounts.TakerAmount, Is.EqualTo(10_000_000));  // 10 shares
    }

    [Test]
    public void Buy_LowestPrice_0_01()
    {
        Assert.That(OrderAmountCalculator.TryCalculateBuy(5f, 0.01, out var amounts, out _), Is.True);
        Assert.That(amounts.MakerAmount, Is.EqualTo(5_000_000));
        Assert.That(amounts.TakerAmount, Is.EqualTo(500_000_000)); // 500 shares
    }

    [Test]
    public void Buy_HighestPrice_0_99_RoundsShares()
    {
        // 5 / 0.99 = 5.0505050505... shares -> 5050505.05 micro -> 5050505
        Assert.That(OrderAmountCalculator.TryCalculateBuy(5f, 0.99, out var amounts, out _), Is.True);
        Assert.That(amounts.MakerAmount, Is.EqualTo(5_000_000));
        Assert.That(amounts.TakerAmount, Is.EqualTo(5_050_505));
    }

    [Test]
    public void Buy_UsesFloatMultiplication_LikeTradeManager()
    {
        // 16.03f is 16.0300006866..., float(16.03f * 1e6) = 16030001.
        // An exact decimal calculation would give 16030000. The class keeps the current behavior.
        Assert.That(OrderAmountCalculator.TryCalculateBuy(16.03f, 0.5, out var amounts, out _), Is.True);
        Assert.That(amounts.MakerAmount, Is.EqualTo(16_030_001));
        Assert.That(amounts.TakerAmount, Is.EqualTo(32_060_001));
    }

    [Test]
    public void Buy_MidpointUsesBankersRounding()
    {
        // 0.0000025f * 1e6 is exactly 2.5 in float. Math.Round rounds to even: 2, not 3.
        Assert.That(OrderAmountCalculator.TryCalculateBuy(0.0000025f, 0.5, out var amounts, out _), Is.True);
        Assert.That(amounts.MakerAmount, Is.EqualTo(2));
        Assert.That(amounts.TakerAmount, Is.EqualTo(5));
    }

    [TestCase(0f)]
    [TestCase(-5f)]
    public void Buy_ZeroOrNegativeAmount_IsRejected(float usd)
    {
        Assert.That(OrderAmountCalculator.TryCalculateBuy(usd, 0.5, out _, out var error), Is.False);
        Assert.That(error, Is.EqualTo("Size is too small."));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void Buy_NotFiniteAmount_IsRejected(float usd)
    {
        Assert.That(OrderAmountCalculator.TryCalculateBuy(usd, 0.5, out _, out _), Is.False);
    }

    [TestCase(0.0)]
    [TestCase(0.000001)]
    [TestCase(-0.5)]
    [TestCase(double.NaN)]
    public void Buy_InvalidAskPrice_IsRejected(double ask)
    {
        Assert.That(OrderAmountCalculator.TryCalculateBuy(5f, ask, out _, out _), Is.False);
    }

    [Test]
    public void Buy_AmountTooLargeForLong_IsRejected()
    {
        // 1e13 USD = 1e19 micro, more than MaxMicroAmount (9e18).
        Assert.That(OrderAmountCalculator.TryCalculateBuy(1e13f, 0.5, out _, out var error), Is.False);
        Assert.That(error, Is.EqualTo("Amount is too large."));
    }

    [TestCase(0.5, 2_500_000)]
    [TestCase(0.01, 50_000)]
    [TestCase(0.99, 4_950_000)]
    public void Sell_FiveShares_ReturnsMicroUnits(double bid, long expectedTaker)
    {
        Assert.That(OrderAmountCalculator.TryCalculateSell(5.0, bid, out var amounts, out _), Is.True);
        Assert.That(amounts.MakerAmount, Is.EqualTo(5_000_000)); // 5 shares
        Assert.That(amounts.TakerAmount, Is.EqualTo(expectedTaker));
    }

    [TestCase(0.0)]
    [TestCase(-1.0)]
    [TestCase(double.NaN)]
    public void Sell_InvalidBidPrice_IsRejected(double bid)
    {
        Assert.That(OrderAmountCalculator.TryCalculateSell(5.0, bid, out _, out _), Is.False);
    }

    [TestCase(0.0)]
    [TestCase(-5.0)]
    [TestCase(double.PositiveInfinity)]
    public void Sell_InvalidShares_IsRejected(double shares)
    {
        Assert.That(OrderAmountCalculator.TryCalculateSell(shares, 0.5, out _, out _), Is.False);
    }
}
