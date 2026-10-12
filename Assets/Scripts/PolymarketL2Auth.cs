using System;
using System.Security.Cryptography;
using System.Text;

public static class PolymarketL2Auth
{
    // Polymarket expects URL-safe base64: '+' -> '-', '/' -> '_', the '=' padding is kept.
    public static string BuildL2Signature(
        string secret,
        string timestamp,
        string method,
        string requestPath,
        string bodyOrNull
    )
    {
        string msg = timestamp + method.ToUpperInvariant() + requestPath + (bodyOrNull ?? "");

        byte[] secretBytes = Convert.FromBase64String(secret);
        byte[] msgBytes = Encoding.UTF8.GetBytes(msg);

        using var hmac = new HMACSHA256(secretBytes);
        byte[] sig = hmac.ComputeHash(msgBytes);

        string b64 = Convert.ToBase64String(sig);

        b64 = b64.Replace('+', '-').Replace('/', '_');

        return b64;
    }
}
