using System.Globalization;
using NUnit.Framework;

// Expected values are worked out by hand with exact decimal arithmetic,
// not taken from the output of OrderAmountCalculator.
public class OrderAmountCalculatorTests
{
    private static decimal D(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    [Test]
    public void Buy_TypicalAmount_ReturnsMicroUnits()
    {
        Assert.That(OrderAmountCalculator.TryCalculateBuy(5m, 0.5m, out var amounts, out _), Is.True);
        Assert.That(amounts.MakerAmount, Is.EqualTo(5_000_000));   // 5 USDC
        Assert.That(amounts.TakerAmount, Is.EqualTo(10_000_000));  // 10 shares
    }

    [Test]
    public void Buy_LowestPrice_0_01()
    {
        Assert.That(OrderAmountCalculator.TryCalculateBuy(5m, 0.01m, out var amounts, out _), Is.True);
        Assert.That(amounts.MakerAmount, Is.EqualTo(5_000_000));
        Assert.That(amounts.TakerAmount, Is.EqualTo(500_000_000)); // 500 shares
    }

    [Test]
    public void Buy_HighestPrice_0_99_RoundsShares()
    {
        // 5 / 0.99 = 5.0505050505... shares -> 5050505.05 micro -> 5050505
        Assert.That(OrderAmountCalculator.TryCalculateBuy(5m, 0.99m, out var amounts, out _), Is.True);
        Assert.That(amounts.MakerAmount, Is.EqualTo(5_000_000));
        Assert.That(amounts.TakerAmount, Is.EqualTo(5_050_505));
    }

    [Test]
    public void Buy_FloatInputFromUi_IsExactInDecimal()
    {
        // 16.03f converted to decimal is exactly 16.03, so makerAmount is 16030000.
        // The old float formula gave 16030001 (one micro-USDC too much).
        Assert.That(OrderAmountCalculator.TryCalculateBuy(16.03f, 0.5, out var amounts, out _), Is.True);
        Assert.That(amounts.MakerAmount, Is.EqualTo(16_030_000));
        Assert.That(amounts.TakerAmount, Is.EqualTo(32_060_000));
    }

    [TestCase("0.0000025", 2)] // 2.5 -> 2
    [TestCase("0.0000035", 4)] // 3.5 -> 4
    public void Buy_MidpointUsesBankersRounding(string usd, long expectedMaker)
    {
        Assert.That(OrderAmountCalculator.TryCalculateBuy(D(usd), 0.5m, out var amounts, out _), Is.True);
        Assert.That(amounts.MakerAmount, Is.EqualTo(expectedMaker));
    }

    [TestCase("0")]
    [TestCase("-5")]
    public void Buy_ZeroOrNegativeAmount_IsRejected(string usd)
    {
        Assert.That(OrderAmountCalculator.TryCalculateBuy(D(usd), 0.5m, out _, out var error), Is.False);
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
        Assert.That(OrderAmountCalculator.TryCalculateBuy(10_000_000_000_000m, 0.5m, out _, out var error), Is.False);
        Assert.That(error, Is.EqualTo("Amount is too large."));
    }

    [TestCase("0.5", 2_500_000)]
    [TestCase("0.01", 50_000)]
    [TestCase("0.99", 4_950_000)]
    public void Sell_FiveShares_ReturnsMicroUnits(string bid, long expectedTaker)
    {
        Assert.That(OrderAmountCalculator.TryCalculateSell(5m, D(bid), out var amounts, out _), Is.True);
        Assert.That(amounts.MakerAmount, Is.EqualTo(5_000_000)); // 5 shares
        Assert.That(amounts.TakerAmount, Is.EqualTo(expectedTaker));
    }

    [TestCase(0.0)]
    [TestCase(-1.0)]
    [TestCase(double.NaN)]
    public void Sell_InvalidBidPrice_IsRejected(double bid)
    {
        Assert.That(OrderAmountCalculator.TryCalculateSell(5m, bid, out _, out _), Is.False);
    }

    [TestCase("0")]
    [TestCase("-5")]
    public void Sell_InvalidShares_IsRejected(string shares)
    {
        Assert.That(OrderAmountCalculator.TryCalculateSell(D(shares), 0.5m, out _, out _), Is.False);
    }

    [Test]
    public void Sell_DecimalOverflow_IsRejectedNotThrown()
    {
        Assert.That(OrderAmountCalculator.TryCalculateSell(1e15m, 1e15m, out _, out var error), Is.False);
        Assert.That(error, Is.EqualTo("Amount is too large."));
    }

    [TestCase("10.529999", "10.52")]
    [TestCase("5", "5")]
    [TestCase("4.999", "4.99")]
    [TestCase("0.009", "0")]
    [TestCase("0", "0")]
    [TestCase("-1", "0")]
    public void RoundDownShares_NeverRoundsUp(string size, string expected)
    {
        Assert.That(OrderAmountCalculator.RoundDownShares(D(size)), Is.EqualTo(D(expected)));
    }

    [Test]
    public void TryToDecimal_KeepsTypedAndParsedValuesExact()
    {
        Assert.That(OrderAmountCalculator.TryToDecimal(16.03f, out decimal usd), Is.True);
        Assert.That(usd, Is.EqualTo(16.03m));
        Assert.That(OrderAmountCalculator.TryToDecimal(0.37, out decimal price), Is.True);
        Assert.That(price, Is.EqualTo(0.37m));
        Assert.That(OrderAmountCalculator.TryToDecimal(double.NaN, out _), Is.False);
    }
}
