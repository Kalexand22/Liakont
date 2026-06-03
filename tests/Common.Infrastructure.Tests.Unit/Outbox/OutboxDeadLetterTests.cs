namespace Stratum.Common.Infrastructure.Tests.Unit.Outbox;

using FluentAssertions;
using Stratum.Common.Infrastructure.Outbox;
using Xunit;

public sealed class OutboxDeadLetterTests
{
    [Fact]
    public void MaxRetries_Should_DefaultToFive()
    {
        var options = new OutboxWorkerOptions();

        options.MaxRetries.Should().Be(5);
    }

    [Fact]
    public void MaxRetries_Should_ReturnConfiguredValue_When_Overridden()
    {
        var options = new OutboxWorkerOptions { MaxRetries = 3 };

        options.MaxRetries.Should().Be(3);
    }

    [Fact]
    public void DeadLetterEvent_Should_RetainAllProperties_When_Constructed()
    {
        var id = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var evt = new DeadLetterEvent
        {
            Id = id,
            EventType = "SomeEvent",
            Payload = """{"key":"value"}""",
            CorrelationId = correlationId,
            ModuleSource = "Party",
            Version = 2,
            OccurredAt = now,
            CreatedAt = now.AddSeconds(-10),
            RetryCount = 5,
            LastError = "Connection refused",
            MovedAt = now,
        };

        evt.Id.Should().Be(id);
        evt.EventType.Should().Be("SomeEvent");
        evt.Payload.Should().Be("""{"key":"value"}""");
        evt.CorrelationId.Should().Be(correlationId);
        evt.ModuleSource.Should().Be("Party");
        evt.Version.Should().Be(2);
        evt.RetryCount.Should().Be(5);
        evt.LastError.Should().Be("Connection refused");
        evt.MovedAt.Should().Be(now);
    }

    [Fact]
    public void LastError_Should_BeNull_When_NotSet()
    {
        var evt = new DeadLetterEvent { LastError = null };

        evt.LastError.Should().BeNull();
    }
}
