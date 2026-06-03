namespace Stratum.Common.Infrastructure.Database;

using FluentAssertions;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

public class OptimisticLockGuardTests
{
    [Fact]
    public void EnsureUpdated_Should_NotThrow_When_OneRowAffected()
    {
        var act = () => OptimisticLockGuard.EnsureUpdated(1, "Party", Guid.NewGuid(), 3);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureUpdated_Should_ThrowConflictException_When_ZeroRowsAffected()
    {
        var id = Guid.NewGuid();

        var act = () => OptimisticLockGuard.EnsureUpdated(0, "Party", id, 5);

        act.Should().Throw<ConflictException>()
            .WithMessage("*Concurrent modification*Party*row version 5*");
    }

    [Fact]
    public void EnsureUpdated_Should_IncludeEntityIdInMessage_When_ZeroRowsAffected()
    {
        var id = Guid.NewGuid();

        var act = () => OptimisticLockGuard.EnsureUpdated(0, "Company", id, 0);

        act.Should().Throw<ConflictException>()
            .WithMessage($"*'{id}'*");
    }

    [Fact]
    public void EnsureUpdated_Should_NotThrow_When_MultipleRowsAffected()
    {
        // Edge case: batch updates that legitimately affect > 1 row should not throw.
        var act = () => OptimisticLockGuard.EnsureUpdated(2, "LineItem", "batch", 1);

        act.Should().NotThrow();
    }
}
