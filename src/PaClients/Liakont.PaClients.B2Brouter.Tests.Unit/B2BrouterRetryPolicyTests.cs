namespace Liakont.PaClients.B2Brouter.Tests.Unit;

using FluentAssertions;
using Xunit;

/// <summary>
/// Tests PAB02 de la politique de retry. La politique de PRODUCTION est figée sur les faits F05 §4.1
/// (3 réessais, backoff 5 s → 30 s → 2 min) — jamais inventée (CLAUDE.md n°2).
/// </summary>
public sealed class B2BrouterRetryPolicyTests
{
    [Fact]
    public void Default_Policy_Matches_The_F05_Backoff_Schedule()
    {
        B2BrouterRetryPolicy.Default.Backoffs.Should().Equal(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2));
        B2BrouterRetryPolicy.Default.RetryCount.Should().Be(3, "3 réessais après la tentative initiale (F05 §4.1)");
    }

    [Fact]
    public void NoDelay_Produces_Zero_Backoffs_For_The_Requested_Count()
    {
        var policy = B2BrouterRetryPolicy.NoDelay(2);

        policy.RetryCount.Should().Be(2);
        policy.Backoffs.Should().AllSatisfy(b => b.Should().Be(TimeSpan.Zero));
    }

    [Fact]
    public void NoDelay_Defaults_To_Three_Retries()
    {
        B2BrouterRetryPolicy.NoDelay().RetryCount.Should().Be(3);
    }

    [Fact]
    public void NoDelay_Rejects_A_Negative_Count()
    {
        var act = () => B2BrouterRetryPolicy.NoDelay(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
