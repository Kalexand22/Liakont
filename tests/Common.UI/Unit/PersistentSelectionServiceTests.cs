namespace Stratum.Common.UI.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Stratum.Common.UI.Services;
using Xunit;

/// <summary>
/// Unit tests for <see cref="PersistentSelectionService{TKey}"/> and
/// <see cref="PersistentSelectionBinding{TItem, TKey}"/> — GUX03.
/// Covers mutation counts, idempotency, Clear, Snapshot immutability, and
/// the SelectionChanged / Changed event contract (fires exactly once per
/// successful mutation, never on no-ops).
/// </summary>
public sealed class PersistentSelectionServiceTests
{
    private static readonly int[] OneTwoThree = [1, 2, 3];
    private static readonly int[] TwoThreeFour = [2, 3, 4];
    private static readonly int[] FourFives = [5, 5, 5, 5];
    private static readonly int[] TwoThreeMissing = [2, 3, 999];
    private static readonly int[] OneTwo = [1, 2];
    private static readonly int[] TwoThree = [2, 3];
    private static readonly int[] TenTwentyThirty = [10, 20, 30];

    private static readonly Row[] TwoRows =
    [
        new Row(1, "Alpha"),
        new Row(2, "Bravo"),
    ];

    private static readonly Row[] TwoRowsOverlap =
    [
        new Row(2, "Bravo"),
        new Row(3, "Charlie"),
    ];

    private static readonly Row[] ThreeRows =
    [
        new Row(1, "a"),
        new Row(2, "b"),
        new Row(3, "c"),
    ];

    private static readonly Row[] RemoveSet =
    [
        new Row(1, "a"),
        new Row(99, "x"),
    ];

    // ── Service: Count / Contains / Snapshot ──────────────────────────────
    [Fact]
    public void NewServiceShouldBeEmpty()
    {
        var svc = new PersistentSelectionService<int>();
        svc.Count.Should().Be(0);
        svc.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void ContainsShouldReturnFalseForMissingKey()
    {
        var svc = new PersistentSelectionService<int>();
        svc.Contains(42).Should().BeFalse();
    }

    [Fact]
    public void ContainsShouldReturnTrueAfterAdd()
    {
        var svc = new PersistentSelectionService<int>();
        svc.Add(42);
        svc.Contains(42).Should().BeTrue();
    }

    [Fact]
    public void SnapshotShouldBeIndependentOfFutureMutations()
    {
        var svc = new PersistentSelectionService<int>();
        svc.AddRange(OneTwoThree);

        var snapshot = svc.Snapshot();

        svc.Add(4);
        svc.Remove(1);

        snapshot.Should().HaveCount(3);
        snapshot.Should().BeEquivalentTo(OneTwoThree);
    }

    // ── Service: Add / AddRange ───────────────────────────────────────────
    [Fact]
    public void AddShouldReturnTrueOnFirstInsertionAndFalseOnDuplicate()
    {
        var svc = new PersistentSelectionService<int>();
        svc.Add(1).Should().BeTrue();
        svc.Add(1).Should().BeFalse("Add is idempotent");
        svc.Count.Should().Be(1);
    }

    [Fact]
    public void AddRangeShouldReturnNumberOfNewlyAddedKeys()
    {
        var svc = new PersistentSelectionService<int>();
        svc.AddRange(OneTwoThree).Should().Be(3);
        svc.AddRange(TwoThreeFour).Should().Be(1, "only 4 was new");
        svc.Count.Should().Be(4);
    }

    [Fact]
    public void AddRangeShouldDeduplicateWithinSingleCall()
    {
        var svc = new PersistentSelectionService<int>();
        svc.AddRange(FourFives).Should().Be(1);
        svc.Count.Should().Be(1);
    }

    // ── Service: Remove / RemoveRange ─────────────────────────────────────
    [Fact]
    public void RemoveShouldReturnFalseForMissingKey()
    {
        var svc = new PersistentSelectionService<int>();
        svc.Remove(1).Should().BeFalse();
    }

    [Fact]
    public void RemoveShouldReturnTrueForPresentKey()
    {
        var svc = new PersistentSelectionService<int>();
        svc.Add(1);
        svc.Remove(1).Should().BeTrue();
        svc.Count.Should().Be(0);
    }

    [Fact]
    public void RemoveRangeShouldReturnNumberOfActuallyRemovedKeys()
    {
        var svc = new PersistentSelectionService<int>();
        svc.AddRange(OneTwoThree);
        svc.RemoveRange(TwoThreeMissing).Should().Be(2, "999 was never present");
        svc.Count.Should().Be(1);
        svc.Contains(1).Should().BeTrue();
    }

    // ── Service: Clear ────────────────────────────────────────────────────
    [Fact]
    public void ClearShouldReturnFalseWhenEmpty()
    {
        var svc = new PersistentSelectionService<int>();
        svc.Clear().Should().BeFalse();
    }

    [Fact]
    public void ClearShouldReturnTrueWhenNonEmptyAndResetCount()
    {
        var svc = new PersistentSelectionService<int>();
        svc.AddRange(OneTwoThree);
        svc.Clear().Should().BeTrue();
        svc.Count.Should().Be(0);
    }

    // ── Service: SelectionChanged event ───────────────────────────────────
    [Fact]
    public void SelectionChangedShouldFireOnceForSuccessfulAdd()
    {
        var svc = new PersistentSelectionService<int>();
        var count = 0;
        svc.SelectionChanged += () => count++;

        svc.Add(1);

        count.Should().Be(1);
    }

    [Fact]
    public void SelectionChangedShouldNotFireWhenAddIsNoOp()
    {
        var svc = new PersistentSelectionService<int>();
        svc.Add(1);

        var count = 0;
        svc.SelectionChanged += () => count++;

        svc.Add(1);

        count.Should().Be(0, "adding a duplicate is a no-op");
    }

    [Fact]
    public void SelectionChangedShouldFireOnceForSuccessfulRemove()
    {
        var svc = new PersistentSelectionService<int>();
        svc.Add(1);

        var count = 0;
        svc.SelectionChanged += () => count++;

        svc.Remove(1);

        count.Should().Be(1);
    }

    [Fact]
    public void SelectionChangedShouldNotFireWhenRemoveIsNoOp()
    {
        var svc = new PersistentSelectionService<int>();
        var count = 0;
        svc.SelectionChanged += () => count++;

        svc.Remove(999);

        count.Should().Be(0);
    }

    [Fact]
    public void SelectionChangedShouldFireOnceForAddRangeWithNewKeys()
    {
        var svc = new PersistentSelectionService<int>();
        var count = 0;
        svc.SelectionChanged += () => count++;

        svc.AddRange(OneTwoThree);

        count.Should().Be(1, "AddRange coalesces into a single notification");
    }

    [Fact]
    public void SelectionChangedShouldNotFireForAddRangeWhenAllKeysAlreadyPresent()
    {
        var svc = new PersistentSelectionService<int>();
        svc.AddRange(OneTwoThree);

        var count = 0;
        svc.SelectionChanged += () => count++;

        svc.AddRange(OneTwoThree);

        count.Should().Be(0);
    }

    [Fact]
    public void SelectionChangedShouldFireOnceForRemoveRangeWithPresentKeys()
    {
        var svc = new PersistentSelectionService<int>();
        svc.AddRange(OneTwoThree);

        var count = 0;
        svc.SelectionChanged += () => count++;

        svc.RemoveRange(TwoThree);

        count.Should().Be(1);
    }

    [Fact]
    public void SelectionChangedShouldFireOnceForClearWhenNonEmpty()
    {
        var svc = new PersistentSelectionService<int>();
        svc.AddRange(OneTwo);

        var count = 0;
        svc.SelectionChanged += () => count++;

        svc.Clear();

        count.Should().Be(1);
    }

    [Fact]
    public void SelectionChangedShouldNotFireForClearWhenAlreadyEmpty()
    {
        var svc = new PersistentSelectionService<int>();
        var count = 0;
        svc.SelectionChanged += () => count++;

        svc.Clear();

        count.Should().Be(0);
    }

    // ── Service: Null-argument guards ─────────────────────────────────────
    [Fact]
    public void AddShouldThrowOnNullReferenceKey()
    {
        var svc = new PersistentSelectionService<string>();
        var act = () => svc.Add(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddRangeShouldThrowWhenKeysIsNull()
    {
        var svc = new PersistentSelectionService<int>();
        var act = () => svc.AddRange(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RemoveRangeShouldThrowWhenKeysIsNull()
    {
        var svc = new PersistentSelectionService<int>();
        var act = () => svc.RemoveRange(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Binding: forwarding through key selector ──────────────────────────
    [Fact]
    public void BindingShouldProjectItemToKeyWhenAdding()
    {
        var svc = new PersistentSelectionService<int>();
        var binding = new PersistentSelectionBinding<Row, int>(svc, r => r.Id);

        binding.Add(new Row(1, "Alpha"));

        svc.Contains(1).Should().BeTrue();
        svc.Count.Should().Be(1);
    }

    [Fact]
    public void BindingAddRangeShouldReturnNewlyAddedCount()
    {
        var svc = new PersistentSelectionService<int>();
        var binding = new PersistentSelectionBinding<Row, int>(svc, r => r.Id);

        binding.AddRange(TwoRows).Should().Be(2);
        binding.AddRange(TwoRowsOverlap).Should().Be(1);
        binding.TotalCount.Should().Be(3);
    }

    [Fact]
    public void BindingRemoveRangeShouldReturnRemovedCount()
    {
        var svc = new PersistentSelectionService<int>();
        var binding = new PersistentSelectionBinding<Row, int>(svc, r => r.Id);
        binding.AddRange(ThreeRows);

        binding.RemoveRange(RemoveSet).Should().Be(1);
        binding.TotalCount.Should().Be(2);
    }

    [Fact]
    public void BindingContainsShouldReflectServiceState()
    {
        var svc = new PersistentSelectionService<int>();
        var binding = new PersistentSelectionBinding<Row, int>(svc, r => r.Id);

        binding.Contains(new Row(1, "a")).Should().BeFalse();
        svc.Add(1);
        binding.Contains(new Row(1, "a")).Should().BeTrue();
    }

    [Fact]
    public void BindingClearShouldEmptyService()
    {
        var svc = new PersistentSelectionService<int>();
        var binding = new PersistentSelectionBinding<Row, int>(svc, r => r.Id);
        binding.AddRange(TwoRows);

        binding.Clear();

        svc.Count.Should().Be(0);
        binding.TotalCount.Should().Be(0);
    }

    [Fact]
    public void BindingChangedEventShouldProxyServiceSelectionChanged()
    {
        var svc = new PersistentSelectionService<int>();
        var binding = new PersistentSelectionBinding<Row, int>(svc, r => r.Id);

        var count = 0;
        binding.Changed += () => count++;

        binding.Add(new Row(1, "a"));

        count.Should().Be(1);
    }

    [Fact]
    public void BindingChangedUnsubscribeShouldStopNotifications()
    {
        var svc = new PersistentSelectionService<int>();
        var binding = new PersistentSelectionBinding<Row, int>(svc, r => r.Id);

        var count = 0;
        Action handler = () => count++;
        binding.Changed += handler;
        binding.Changed -= handler;

        binding.Add(new Row(1, "a"));

        count.Should().Be(0, "unsubscription must be forwarded to the service");
    }

    [Fact]
    public void BindingTotalCountShouldTrackService()
    {
        var svc = new PersistentSelectionService<int>();
        var binding = new PersistentSelectionBinding<Row, int>(svc, r => r.Id);

        binding.TotalCount.Should().Be(0);
        svc.AddRange(TenTwentyThirty);
        binding.TotalCount.Should().Be(3);
    }

    [Fact]
    public void BindingConstructorShouldThrowWhenServiceIsNull()
    {
        var act = () => new PersistentSelectionBinding<Row, int>(null!, r => r.Id);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BindingConstructorShouldThrowWhenKeySelectorIsNull()
    {
        var svc = new PersistentSelectionService<int>();
        var act = () => new PersistentSelectionBinding<Row, int>(svc, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private sealed record Row(int Id, string Label);
}
