namespace Liakont.Modules.Ingestion.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Ingestion.Domain.Entities;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

public sealed class AgentTests
{
    [Fact]
    public void Create_Generates_Prefix_Hash_And_Reveals_FullKey_Once()
    {
        var (agent, fullKey) = Agent.Create("acme", "Poste de vente 1");

        agent.TenantId.Should().Be("acme");
        agent.Name.Should().Be("Poste de vente 1");
        agent.IsRevoked.Should().BeFalse();
        agent.RevokedAt.Should().BeNull();
        agent.LastSeenAtUtc.Should().BeNull();

        agent.KeyPrefix.Should().StartWith("agt_");
        agent.KeyHash.Should().HaveLength(64, "l'empreinte est un SHA-256 en hexadécimal.");

        // La clé complète est au format prefix.secret et n'est jamais stockée en clair.
        fullKey.Should().StartWith(agent.KeyPrefix + ".");
        fullKey.Should().NotBe(agent.KeyHash);
        agent.KeyHash.Should().NotContain(fullKey);
    }

    [Fact]
    public void MatchesPresentedKey_True_For_Issued_Key_Only()
    {
        var (agent, fullKey) = Agent.Create("acme", "Poste 1");

        agent.MatchesPresentedKey(fullKey).Should().BeTrue();
        agent.MatchesPresentedKey(fullKey + "x").Should().BeFalse();
        agent.MatchesPresentedKey("agt_autre.secret").Should().BeFalse();
    }

    [Fact]
    public void Create_Produces_Distinct_Keys()
    {
        var (first, firstKey) = Agent.Create("acme", "A");
        var (second, secondKey) = Agent.Create("acme", "B");

        first.KeyPrefix.Should().NotBe(second.KeyPrefix);
        firstKey.Should().NotBe(secondKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sansseparateur")]
    [InlineData(".secretsansprefixe")]
    [InlineData("prefixesanssecret.")]
    public void TryExtractKeyPrefix_Rejects_Malformed_Keys(string? presented)
    {
        Agent.TryExtractKeyPrefix(presented, out var prefix).Should().BeFalse();
        prefix.Should().BeEmpty();
    }

    [Fact]
    public void TryExtractKeyPrefix_Returns_Prefix_Of_Issued_Key()
    {
        var (agent, fullKey) = Agent.Create("acme", "Poste 1");

        Agent.TryExtractKeyPrefix(fullKey, out var prefix).Should().BeTrue();
        prefix.Should().Be(agent.KeyPrefix);
    }

    [Fact]
    public void Revoke_Sets_Flags_And_Is_Idempotent()
    {
        var (agent, _) = Agent.Create("acme", "Poste 1");

        agent.Revoke();
        agent.IsRevoked.Should().BeTrue();
        agent.RevokedAt.Should().NotBeNull();

        var firstRevokedAt = agent.RevokedAt;
        agent.Revoke();
        agent.RevokedAt.Should().Be(firstRevokedAt, "la révocation est idempotente.");
    }

    [Fact]
    public void RotateKey_Changes_Key_And_Invalidates_The_Old_One()
    {
        var (agent, oldKey) = Agent.Create("acme", "Poste 1");
        var oldPrefix = agent.KeyPrefix;

        var newKey = agent.RotateKey();

        agent.KeyPrefix.Should().NotBe(oldPrefix);
        newKey.Should().NotBe(oldKey);
        agent.MatchesPresentedKey(newKey).Should().BeTrue();
        agent.MatchesPresentedKey(oldKey).Should().BeFalse("l'ancienne clé cesse d'être valide.");
    }

    [Fact]
    public void RotateKey_On_Revoked_Agent_Throws_Conflict()
    {
        var (agent, _) = Agent.Create("acme", "Poste 1");
        agent.Revoke();

        var act = agent.RotateKey;

        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void RecordHeartbeat_Updates_LastSeen_And_Version()
    {
        var (agent, _) = Agent.Create("acme", "Poste 1");
        var firstSeen = new DateTimeOffset(2026, 6, 4, 10, 0, 0, TimeSpan.Zero);

        agent.RecordHeartbeat("2.3.1", firstSeen);
        agent.LastSeenAtUtc.Should().Be(firstSeen);
        agent.LastAgentVersion.Should().Be("2.3.1");

        // Une version vide ne doit pas effacer la dernière version connue, mais la dernière vue avance.
        var secondSeen = firstSeen.AddMinutes(15);
        agent.RecordHeartbeat("   ", secondSeen);
        agent.LastSeenAtUtc.Should().Be(secondSeen);
        agent.LastAgentVersion.Should().Be("2.3.1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Empty_Name(string name)
    {
        var act = () => Agent.Create("acme", name);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Empty_Tenant(string tenantId)
    {
        var act = () => Agent.Create(tenantId, "Poste 1");

        act.Should().Throw<ArgumentException>();
    }
}
