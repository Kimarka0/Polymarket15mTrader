using System;
using NUnit.Framework;

public class OrderBookCheckTests
{
    private static readonly DateTime Now = new DateTime(2026, 10, 12, 12, 0, 10, DateTimeKind.Utc);
    private const string Token = "token-up";

    private static bool Check(string assetId, string[] prices, bool lowest, DateTime loadedAt, out decimal price, out string reason)
    {
        return OrderBookCheck.TryGetBestPrice(Token, assetId, prices, lowest, loadedAt, Now, out price, out reason);
    }

    [Test]
    public void BestAsk_IsTheLowestPrice()
    {
        Assert.That(Check(Token, new[] { "0.55", "0.53", "0.60" }, true, Now.AddSeconds(-1), out decimal price, out _), Is.True);
        Assert.That(price, Is.EqualTo(0.53m));
    }

    [Test]
    public void BestBid_IsTheHighestPrice()
    {
        Assert.That(Check(Token, new[] { "0.40", "0.47", "0.45" }, false, Now.AddSeconds(-1), out decimal price, out _), Is.True);
        Assert.That(price, Is.EqualTo(0.47m));
    }

    [Test]
    public void BookOfAnotherToken_IsRejected()
    {
        Assert.That(Check("token-of-old-market", new[] { "0.99" }, true, Now, out _, out string reason), Is.False);
        Assert.That(reason, Does.Contain("another token"));
    }

    [Test]
    public void BookWithoutAssetId_IsRejected()
    {
        Assert.That(Check(null, new[] { "0.5" }, true, Now, out _, out string reason), Is.False);
        Assert.That(reason, Does.Contain("asset_id"));
    }

    [Test]
    public void NotLoadedBook_IsRejected()
    {
        Assert.That(Check(Token, new[] { "0.5" }, true, default, out _, out string reason), Is.False);
        Assert.That(reason, Does.Contain("not loaded"));
    }

    [Test]
    public void StaleBook_IsRejected()
    {
        Assert.That(Check(Token, new[] { "0.5" }, true, Now.AddSeconds(-6), out _, out string reason), Is.False);
        Assert.That(reason, Does.Contain("6 s old"));
    }

    [Test]
    public void BookExactlyAtTheAgeLimit_IsAccepted()
    {
        Assert.That(Check(Token, new[] { "0.5" }, true, Now.AddSeconds(-OrderBookCheck.MaxAgeSeconds), out _, out _), Is.True);
    }

    [Test]
    public void EmptyOrUnparseableSide_IsRejected()
    {
        Assert.That(Check(Token, new string[0], true, Now, out _, out string asks), Is.False);
        Assert.That(asks, Does.Contain("no asks"));
        Assert.That(Check(Token, new[] { "abc", null, "0" }, false, Now, out _, out string bids), Is.False);
        Assert.That(bids, Does.Contain("no bids"));
    }
}
