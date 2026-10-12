using NUnit.Framework;

public class OrderSendOutcomeTests
{
    [Test]
    public void NoResponse_TimeoutOrNetworkError_IsUnknown()
    {
        Assert.That(OrderSendOutcome.Classify(0, false, false), Is.EqualTo(OrderSendResult.Unknown));
    }

    [TestCase(500)]
    [TestCase(502)]
    [TestCase(504)]
    public void ServerError_IsUnknown(long status)
    {
        Assert.That(OrderSendOutcome.Classify(status, false, false), Is.EqualTo(OrderSendResult.Unknown));
    }

    [TestCase(400)]
    [TestCase(401)]
    [TestCase(429)]
    public void ClientError_IsRejected(long status)
    {
        Assert.That(OrderSendOutcome.Classify(status, false, false), Is.EqualTo(OrderSendResult.Rejected));
    }

    [Test]
    public void Ok_WithSuccess_IsAccepted()
    {
        Assert.That(OrderSendOutcome.Classify(200, true, true), Is.EqualTo(OrderSendResult.Accepted));
    }

    [Test]
    public void Ok_WithSuccessFalse_IsRejected()
    {
        // For example a FOK order that could not be filled.
        Assert.That(OrderSendOutcome.Classify(200, true, false), Is.EqualTo(OrderSendResult.Rejected));
    }

    [Test]
    public void Ok_ButUnreadableBody_IsUnknown()
    {
        Assert.That(OrderSendOutcome.Classify(200, false, false), Is.EqualTo(OrderSendResult.Unknown));
    }
}
