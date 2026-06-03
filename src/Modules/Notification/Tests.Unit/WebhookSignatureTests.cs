namespace Stratum.Modules.Notification.Tests.Unit;

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Stratum.Modules.Notification.Domain.Services;
using Xunit;

public class WebhookSignatureTests
{
    [Fact]
    public void Compute_Should_Return_Sha256_Prefixed_Hex()
    {
        var payload = """{"event":"test"}""";
        var secret = "abcdefghijklmnopqrstuvwxyz0123456789";

        var result = WebhookSignature.Compute(payload, secret);

        result.Should().StartWith("sha256=");
        result[7..].Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Compute_Should_Produce_Deterministic_Output()
    {
        var payload = """{"event":"test","id":"123"}""";
        var secret = "abcdefghijklmnopqrstuvwxyz0123456789";

        var result1 = WebhookSignature.Compute(payload, secret);
        var result2 = WebhookSignature.Compute(payload, secret);

        result1.Should().Be(result2);
    }

    [Fact]
    public void Compute_Should_Differ_With_Different_Secret()
    {
        var payload = """{"event":"test"}""";
        var secret1 = "abcdefghijklmnopqrstuvwxyz012345_1";
        var secret2 = "abcdefghijklmnopqrstuvwxyz012345_2";

        var result1 = WebhookSignature.Compute(payload, secret1);
        var result2 = WebhookSignature.Compute(payload, secret2);

        result1.Should().NotBe(result2);
    }

    [Fact]
    public void Compute_Should_Match_Manual_HmacSha256()
    {
        var payload = """{"data":"value"}""";
        var secret = "abcdefghijklmnopqrstuvwxyz0123456789";

        var expected = "sha256=" + Convert.ToHexStringLower(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                Encoding.UTF8.GetBytes(payload)));

        var result = WebhookSignature.Compute(payload, secret);

        result.Should().Be(expected);
    }
}
