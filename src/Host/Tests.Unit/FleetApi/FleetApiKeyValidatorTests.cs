namespace Liakont.Host.Tests.Unit.FleetApi;

using FluentAssertions;
using Liakont.Host.FleetApi;
using Xunit;

/// <summary>
/// Validation de la clé d'ingestion du heartbeat de flotte (OPS04) : une clé configurée vide ou une clé
/// présentée vide refuse l'accès ; seule la clé EXACTE est autorisée (comparaison à temps constant).
/// </summary>
public sealed class FleetApiKeyValidatorTests
{
    [Fact]
    public void Authorizes_Exact_Match()
    {
        FleetApiKeyValidator.IsAuthorized("s3cr3t-key", "s3cr3t-key").Should().BeTrue();
    }

    [Theory]
    [InlineData("s3cr3t-key", "wrong")]
    [InlineData("s3cr3t-key", "s3cr3t-ke")]
    [InlineData("s3cr3t-key", "S3CR3T-KEY")]
    public void Rejects_Mismatch(string configured, string provided)
    {
        FleetApiKeyValidator.IsAuthorized(configured, provided).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "anything")]
    [InlineData("", "anything")]
    [InlineData("configured", null)]
    [InlineData("configured", "")]
    public void Rejects_When_Either_Key_Is_Empty(string? configured, string? provided)
    {
        FleetApiKeyValidator.IsAuthorized(configured, provided).Should().BeFalse();
    }
}
