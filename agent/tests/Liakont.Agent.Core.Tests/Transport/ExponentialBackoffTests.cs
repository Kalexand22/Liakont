namespace Liakont.Agent.Core.Tests.Transport;

using System;
using FluentAssertions;
using Liakont.Agent.Core.Transport;
using Xunit;

/// <summary>Backoff exponentiel (F12 §3.3) : croissance × 2 par tentative, plafonné, première tentative = délai de base.</summary>
public class ExponentialBackoffTests
{
    [Fact]
    public void Delay_grows_exponentially_from_the_base()
    {
        var backoff = new ExponentialBackoff(TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(5));

        backoff.DelayFor(1).Should().Be(TimeSpan.FromSeconds(2));
        backoff.DelayFor(2).Should().Be(TimeSpan.FromSeconds(4));
        backoff.DelayFor(3).Should().Be(TimeSpan.FromSeconds(8));
        backoff.DelayFor(4).Should().Be(TimeSpan.FromSeconds(16));
    }

    [Fact]
    public void Delay_is_capped_at_the_maximum()
    {
        var backoff = new ExponentialBackoff(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));

        backoff.DelayFor(10).Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Attempt_below_one_is_rejected()
    {
        var backoff = new ExponentialBackoff();

        Action act = () => backoff.DelayFor(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Invalid_bounds_are_rejected()
    {
        Action negativeBase = () => _ = new ExponentialBackoff(TimeSpan.Zero, TimeSpan.FromMinutes(1));
        Action maxBelowBase = () => _ = new ExponentialBackoff(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));

        negativeBase.Should().Throw<ArgumentOutOfRangeException>();
        maxBelowBase.Should().Throw<ArgumentOutOfRangeException>();
    }
}
