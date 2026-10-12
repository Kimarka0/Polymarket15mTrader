// What we know about an order after POST /order. No UnityEngine: tested in OrderMath.
public enum OrderSendResult
{
    Accepted,  // the exchange answered and filled the order
    Rejected,  // the exchange answered and did not take the order: safe to try again
    Unknown    // no clear answer: the order may or may not have been placed
}

public static class OrderSendOutcome
{
    // httpStatus is 0 when no HTTP response arrived (timeout, lost connection).
    public static OrderSendResult Classify(long httpStatus, bool responseParsed, bool success)
    {
        if (httpStatus == 0) return OrderSendResult.Unknown;     // the request may have reached the exchange
        if (httpStatus >= 500) return OrderSendResult.Unknown;   // server error after the request was received
        if (httpStatus >= 400) return OrderSendResult.Rejected;  // the exchange refused the order
        if (httpStatus < 200 || httpStatus >= 300) return OrderSendResult.Unknown;
        if (!responseParsed) return OrderSendResult.Unknown;
        return success ? OrderSendResult.Accepted : OrderSendResult.Rejected;
    }
}
