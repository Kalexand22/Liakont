namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Domain;
using Xunit;

/// <summary>Tests des modes d'ancrage NoAnchor et OpenTimestamps (TRK06).</summary>
public sealed class TimestampAnchorModesTests
{
    [Fact]
    public async Task NoAnchor_IsOperational_ButProducesNoProof()
    {
        var anchor = new NoAnchorTimestampAnchor();

        anchor.Capabilities.Method.Should().Be(TimestampAnchorMethod.None);
        anchor.Capabilities.IsOperational.Should().BeTrue();
        anchor.Capabilities.RequiresOutboundInternet.Should().BeFalse();

        TimestampAnchorResult result = await anchor.AnchorAsync(new byte[32]);

        result.IsAnchored.Should().BeFalse();
        result.Proof.Should().BeNull();
    }

    [Fact]
    public async Task OpenTimestamps_IsDeferred_AndThrowsOnUse()
    {
        var anchor = new OpenTimestampsTimestampAnchor();

        anchor.Capabilities.Method.Should().Be(TimestampAnchorMethod.OpenTimestamps);
        anchor.Capabilities.IsOperational.Should().BeFalse();

        Func<Task> anchorAct = () => anchor.AnchorAsync(new byte[32]);
        (await anchorAct.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("V1.1");

        Func<Task> verifyAct = () => anchor.VerifyAsync(new byte[1], new byte[32]);
        await verifyAct.Should().ThrowAsync<NotSupportedException>();
    }
}
