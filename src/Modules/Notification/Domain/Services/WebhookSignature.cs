namespace Stratum.Modules.Notification.Domain.Services;

using System.Security.Cryptography;
using System.Text;

public static class WebhookSignature
{
    public static string Compute(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);

        return "sha256=" + Convert.ToHexStringLower(hash);
    }
}
