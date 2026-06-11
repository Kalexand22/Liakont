namespace Liakont.PaClients.SuperPdp.Tests.Unit;

using FluentAssertions;
using Xunit;

/// <summary>Couvre la politique de retry des erreurs transitoires (F14 §4.1).</summary>
public sealed class SuperPdpRetryPolicyTests
{
    [Fact]
    public void Default_Policy_Has_Three_Backoffs()
    {
        var policy = SuperPdpRetryPolicy.Default;

        policy.RetryCount.Should().Be(3);
        policy.Backoffs.Should().Equal(
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void NoDelay_Produces_Zero_Backoffs(int retries)
    {
        var policy = SuperPdpRetryPolicy.NoDelay(retries);

        policy.RetryCount.Should().Be(retries);
        policy.Backoffs.Should().OnlyContain(b => b == TimeSpan.Zero);
    }

    [Fact]
    public void NoDelay_Defaults_To_Three_Retries()
    {
        SuperPdpRetryPolicy.NoDelay().RetryCount.Should().Be(3);
    }

    [Fact]
    public void NoDelay_With_Negative_Retries_Throws()
    {
        var act = () => SuperPdpRetryPolicy.NoDelay(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
