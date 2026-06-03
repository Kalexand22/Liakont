namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

public sealed class FilterExpressionBuilderTests
{
    private static readonly string[] InSetValues = { "Alice", "Bob" };
    private static readonly string[] NotInNullTestValues = { "Alice", "Bob" };

    private readonly FilterExpressionBuilder<TestEntity> _builder = new();

    [Fact]
    public void EqualsShouldMatchExactValue()
    {
        var group = SingleCriterion("Name", FilterOperator.Equals, "Alice");

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Bob" }).Should().BeFalse();
    }

    [Fact]
    public void NotEqualsShouldExcludeValue()
    {
        var group = SingleCriterion("Name", FilterOperator.NotEquals, "Alice");

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeFalse();
        predicate(new TestEntity { Name = "Bob" }).Should().BeTrue();
    }

    [Fact]
    public void ContainsShouldMatchSubstring()
    {
        var group = SingleCriterion("Name", FilterOperator.Contains, "li");

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Bob" }).Should().BeFalse();
    }

    [Fact]
    public void ContainsShouldReturnFalseForNullProperty()
    {
        var group = SingleCriterion("Name", FilterOperator.Contains, "test");

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Name = null! }).Should().BeFalse();
    }

    [Fact]
    public void StartsWithShouldMatchPrefix()
    {
        var group = SingleCriterion("Name", FilterOperator.StartsWith, "Al");

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Bob" }).Should().BeFalse();
    }

    [Fact]
    public void GreaterThanShouldCompareValues()
    {
        var group = SingleCriterion("Amount", FilterOperator.GreaterThan, 100m);

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Amount = 150m }).Should().BeTrue();
        predicate(new TestEntity { Amount = 50m }).Should().BeFalse();
        predicate(new TestEntity { Amount = 100m }).Should().BeFalse();
    }

    [Fact]
    public void LessThanShouldCompareValues()
    {
        var group = SingleCriterion("Amount", FilterOperator.LessThan, 100m);

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Amount = 50m }).Should().BeTrue();
        predicate(new TestEntity { Amount = 150m }).Should().BeFalse();
    }

    [Fact]
    public void BetweenShouldMatchInclusiveRange()
    {
        var criterion = new FilterCriterion("Amount", FilterOperator.Between, 100m, 200m);
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion> { criterion });

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Amount = 100m }).Should().BeTrue();
        predicate(new TestEntity { Amount = 150m }).Should().BeTrue();
        predicate(new TestEntity { Amount = 200m }).Should().BeTrue();
        predicate(new TestEntity { Amount = 50m }).Should().BeFalse();
        predicate(new TestEntity { Amount = 250m }).Should().BeFalse();
    }

    [Fact]
    public void InShouldMatchSetOfValues()
    {
        var criterion = new FilterCriterion("Name", FilterOperator.In, InSetValues);
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion> { criterion });

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Bob" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Charlie" }).Should().BeFalse();
    }

    [Fact]
    public void IsNullShouldMatchNullableProperty()
    {
        var group = SingleCriterion("DueDate", FilterOperator.IsNull, null);

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = null }).Should().BeTrue();
        predicate(new TestEntity { DueDate = DateTime.Today }).Should().BeFalse();
    }

    [Fact]
    public void IsNotNullShouldMatchNonNullProperty()
    {
        var group = SingleCriterion("DueDate", FilterOperator.IsNotNull, null);

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = DateTime.Today }).Should().BeTrue();
        predicate(new TestEntity { DueDate = null }).Should().BeFalse();
    }

    [Fact]
    public void IsNullOnNonNullableValueTypeShouldReturnFalse()
    {
        var group = SingleCriterion("Amount", FilterOperator.IsNull, null);

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Amount = 0m }).Should().BeFalse();
    }

    [Fact]
    public void AndLogicShouldRequireAllCriteria()
    {
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Contains, "A"),
            new("Amount", FilterOperator.GreaterThan, 100m),
        });

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Name = "Alice", Amount = 200m }).Should().BeTrue();
        predicate(new TestEntity { Name = "Alice", Amount = 50m }).Should().BeFalse();
        predicate(new TestEntity { Name = "Bob", Amount = 200m }).Should().BeFalse();
    }

    [Fact]
    public void OrLogicShouldRequireAnyCriterion()
    {
        var group = new FilterGroup(FilterLogic.Or, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "Alice"),
            new("Name", FilterOperator.Equals, "Bob"),
        });

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Bob" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Charlie" }).Should().BeFalse();
    }

    [Fact]
    public void NestedGroupsShouldCombineCorrectly()
    {
        // (Name contains "A" AND Amount > 100) OR (Name = "Bob")
        var inner1 = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Contains, "A"),
            new("Amount", FilterOperator.GreaterThan, 100m),
        });
        var inner2 = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "Bob"),
        });

        var outer = new FilterGroup(FilterLogic.Or, new List<FilterCriterion>(), new List<FilterGroup> { inner1, inner2 });

        var predicate = _builder.Build(outer).Compile();

        predicate(new TestEntity { Name = "Alice", Amount = 200m }).Should().BeTrue();
        predicate(new TestEntity { Name = "Bob", Amount = 0m }).Should().BeTrue();
        predicate(new TestEntity { Name = "Alice", Amount = 50m }).Should().BeFalse();
        predicate(new TestEntity { Name = "Charlie", Amount = 200m }).Should().BeFalse();
    }

    [Fact]
    public void ShouldResolveRelatedTablePropertyPath()
    {
        var group = SingleCriterion("Customer.City", FilterOperator.Equals, "Paris");

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Customer = new RelatedEntity { City = "Paris" } }).Should().BeTrue();
        predicate(new TestEntity { Customer = new RelatedEntity { City = "London" } }).Should().BeFalse();
    }

    [Fact]
    public void ShouldSupportContainsOnRelatedTableProperty()
    {
        var group = SingleCriterion("Customer.City", FilterOperator.Contains, "ar");

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Customer = new RelatedEntity { City = "Paris" } }).Should().BeTrue();
        predicate(new TestEntity { Customer = new RelatedEntity { City = "London" } }).Should().BeFalse();
    }

    [Fact]
    public void ShouldThrowForInvalidPropertyPath()
    {
        var group = SingleCriterion("NonExistent", FilterOperator.Equals, "x");

        var act = () => _builder.Build(group);

        act.Should().Throw<ArgumentException>().WithMessage("*'NonExistent'*not found*");
    }

    [Fact]
    public void ShouldThrowForEmptyField()
    {
        var group = SingleCriterion(string.Empty, FilterOperator.Equals, "x");

        var act = () => _builder.Build(group);

        act.Should().Throw<ArgumentException>().WithMessage("*must not be empty*");
    }

    [Fact]
    public void ShouldThrowForBetweenWithoutValueEnd()
    {
        var criterion = new FilterCriterion("Amount", FilterOperator.Between, 100m, null);
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion> { criterion });

        var act = () => _builder.Build(group).Compile()(new TestEntity());

        act.Should().Throw<ArgumentException>().WithMessage("*ValueEnd*");
    }

    [Fact]
    public void ShouldThrowForContainsOnNonStringProperty()
    {
        var group = SingleCriterion("Amount", FilterOperator.Contains, "test");

        var act = () => _builder.Build(group).Compile()(new TestEntity());

        act.Should().Throw<InvalidOperationException>().WithMessage("*string*");
    }

    [Fact]
    public void EmptyGroupShouldReturnTruePredicate()
    {
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion>());

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity()).Should().BeTrue();
    }

    [Fact]
    public void NotContainsShouldExcludeSubstring()
    {
        var group = SingleCriterion("Name", FilterOperator.NotContains, "li");

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeFalse();
        predicate(new TestEntity { Name = "Bob" }).Should().BeTrue();
    }

    [Fact]
    public void NotContainsShouldReturnFalseForNullProperty()
    {
        var group = SingleCriterion("Name", FilterOperator.NotContains, "test");

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Name = null! }).Should().BeFalse();
    }

    [Fact]
    public void EndsWithShouldMatchSuffix()
    {
        var group = SingleCriterion("Name", FilterOperator.EndsWith, "ce");

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeTrue();
        predicate(new TestEntity { Name = "Bob" }).Should().BeFalse();
    }

    [Fact]
    public void NotInShouldExcludeSetValues()
    {
        var criterion = new FilterCriterion("Name", FilterOperator.NotIn, InSetValues);
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion> { criterion });

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Name = "Alice" }).Should().BeFalse();
        predicate(new TestEntity { Name = "Bob" }).Should().BeFalse();
        predicate(new TestEntity { Name = "Charlie" }).Should().BeTrue();
    }

    [Fact]
    public void GreaterThanOrEqualShouldIncludeBoundary()
    {
        var group = SingleCriterion("Amount", FilterOperator.GreaterThanOrEqual, 100m);

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Amount = 100m }).Should().BeTrue();
        predicate(new TestEntity { Amount = 150m }).Should().BeTrue();
        predicate(new TestEntity { Amount = 50m }).Should().BeFalse();
    }

    [Fact]
    public void LessThanOrEqualShouldIncludeBoundary()
    {
        var group = SingleCriterion("Amount", FilterOperator.LessThanOrEqual, 100m);

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Amount = 100m }).Should().BeTrue();
        predicate(new TestEntity { Amount = 50m }).Should().BeTrue();
        predicate(new TestEntity { Amount = 150m }).Should().BeFalse();
    }

    [Fact]
    public void NotBetweenShouldExcludeRange()
    {
        var criterion = new FilterCriterion("Amount", FilterOperator.NotBetween, 100m, 200m);
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion> { criterion });

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Amount = 50m }).Should().BeTrue();
        predicate(new TestEntity { Amount = 250m }).Should().BeTrue();
        predicate(new TestEntity { Amount = 100m }).Should().BeFalse();
        predicate(new TestEntity { Amount = 150m }).Should().BeFalse();
        predicate(new TestEntity { Amount = 200m }).Should().BeFalse();
    }

    [Fact]
    public void BeforeShouldMatchEarlierDates()
    {
        var cutoff = new DateTime(2026, 6, 15);
        var group = SingleCriterion("DueDate", FilterOperator.Before, cutoff);

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 10) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 15) }).Should().BeFalse();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 20) }).Should().BeFalse();
    }

    [Fact]
    public void AfterShouldMatchLaterDates()
    {
        var cutoff = new DateTime(2026, 6, 15);
        var group = SingleCriterion("DueDate", FilterOperator.After, cutoff);

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 20) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 15) }).Should().BeFalse();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 10) }).Should().BeFalse();
    }

    [Fact]
    public void RelativePeriodTodayShouldMatchCurrentDay()
    {
        var today = DateTime.UtcNow.Date;
        var group = SingleCriterion("DueDate", FilterOperator.RelativePeriod, RelativeDatePeriod.Today);

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = today.AddHours(12) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = today.AddDays(-1) }).Should().BeFalse();
        predicate(new TestEntity { DueDate = today.AddDays(1) }).Should().BeFalse();
    }

    [Fact]
    public void RelativePeriodFromStringShouldWork()
    {
        var today = DateTime.UtcNow.Date;
        var group = SingleCriterion("DueDate", FilterOperator.RelativePeriod, "Today");

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = today.AddHours(12) }).Should().BeTrue();
    }

    [Fact]
    public void NotBetweenShouldReturnFalseForNullProperty()
    {
        var criterion = new FilterCriterion(
            "DueDate",
            FilterOperator.NotBetween,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31));
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion> { criterion });

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = null }).Should().BeFalse();
    }

    [Fact]
    public void NotInShouldReturnFalseForNullStringProperty()
    {
        var criterion = new FilterCriterion("Name", FilterOperator.NotIn, NotInNullTestValues);
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion> { criterion });

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { Name = null! }).Should().BeFalse();
    }

    [Fact]
    public void RelativePeriodShouldReturnFalseForNullDate()
    {
        var group = SingleCriterion("DueDate", FilterOperator.RelativePeriod, RelativeDatePeriod.Today);

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = null }).Should().BeFalse();
    }

    // ── DF-11: DateOnly → full-day boundary expansion on DateTime column ──────
    [Fact]
    public void DateOnlyEqualsShouldCoverEntireDay()
    {
        var group = SingleCriterion("DueDate", FilterOperator.Equals, new DateOnly(2026, 6, 15));

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 15, 12, 30, 0, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 15, 23, 59, 59, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 14, 23, 59, 59, DateTimeKind.Utc) }).Should().BeFalse();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc) }).Should().BeFalse();
    }

    [Fact]
    public void DateOnlyNotEqualsShouldExcludeEntireDay()
    {
        var group = SingleCriterion("DueDate", FilterOperator.NotEquals, new DateOnly(2026, 6, 15));

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc) }).Should().BeFalse();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = null }).Should().BeFalse();
    }

    [Fact]
    public void DateOnlyBeforeShouldExcludeSameDay()
    {
        var group = SingleCriterion("DueDate", FilterOperator.Before, new DateOnly(2026, 6, 15));

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 14, 23, 59, 59, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc) }).Should().BeFalse();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 15, 23, 59, 59, DateTimeKind.Utc) }).Should().BeFalse();
    }

    [Fact]
    public void DateOnlyAfterShouldExcludeSameDay()
    {
        var group = SingleCriterion("DueDate", FilterOperator.After, new DateOnly(2026, 6, 15));

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 15, 23, 59, 59, DateTimeKind.Utc) }).Should().BeFalse();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc) }).Should().BeFalse();
    }

    [Fact]
    public void DateOnlyBetweenShouldIncludeBothBoundaryDaysEntirely()
    {
        var criterion = new FilterCriterion(
            "DueDate",
            FilterOperator.Between,
            new DateOnly(2026, 6, 10),
            new DateOnly(2026, 6, 15));
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion> { criterion });

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 15, 23, 59, 59, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 12, 6, 0, 0, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 9, 23, 59, 59, DateTimeKind.Utc) }).Should().BeFalse();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc) }).Should().BeFalse();
    }

    [Fact]
    public void DateOnlyNotBetweenShouldExcludeBoundaryDays()
    {
        var criterion = new FilterCriterion(
            "DueDate",
            FilterOperator.NotBetween,
            new DateOnly(2026, 6, 10),
            new DateOnly(2026, 6, 15));
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion> { criterion });

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc) }).Should().BeFalse();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 9, 23, 59, 59, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = null }).Should().BeFalse();
    }

    [Fact]
    public void DateOnlyGreaterThanOrEqualShouldIncludeStartOfSameDay()
    {
        var group = SingleCriterion("DueDate", FilterOperator.GreaterThanOrEqual, new DateOnly(2026, 6, 15));

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 14, 23, 59, 59, DateTimeKind.Utc) }).Should().BeFalse();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc) }).Should().BeTrue();
    }

    [Fact]
    public void DateOnlyLessThanOrEqualShouldIncludeEndOfSameDay()
    {
        var group = SingleCriterion("DueDate", FilterOperator.LessThanOrEqual, new DateOnly(2026, 6, 15));

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 15, 23, 59, 59, DateTimeKind.Utc) }).Should().BeTrue();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc) }).Should().BeFalse();
        predicate(new TestEntity { DueDate = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc) }).Should().BeTrue();
    }

    // ── DF-11: DateOnly normalization against DateTimeOffset columns ──────
    [Fact]
    public void DateOnlyEqualsShouldCoverEntireDayOnDateTimeOffset()
    {
        var group = SingleCriterion("OffsetDueDate", FilterOperator.Equals, new DateOnly(2026, 6, 15));

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { OffsetDueDate = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero) }).Should().BeTrue();
        predicate(new TestEntity { OffsetDueDate = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero) }).Should().BeTrue();
        predicate(new TestEntity { OffsetDueDate = new DateTimeOffset(2026, 6, 15, 23, 59, 59, TimeSpan.Zero) }).Should().BeTrue();
        predicate(new TestEntity { OffsetDueDate = new DateTimeOffset(2026, 6, 14, 23, 59, 59, TimeSpan.Zero) }).Should().BeFalse();
        predicate(new TestEntity { OffsetDueDate = new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero) }).Should().BeFalse();
    }

    [Fact]
    public void DateOnlyBetweenShouldIncludeBothBoundariesOnDateTimeOffset()
    {
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("OffsetDueDate", FilterOperator.Between, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 15)),
        });

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { OffsetDueDate = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero) }).Should().BeTrue();
        predicate(new TestEntity { OffsetDueDate = new DateTimeOffset(2026, 6, 15, 23, 59, 59, TimeSpan.Zero) }).Should().BeTrue();
        predicate(new TestEntity { OffsetDueDate = new DateTimeOffset(2026, 6, 9, 23, 59, 59, TimeSpan.Zero) }).Should().BeFalse();
        predicate(new TestEntity { OffsetDueDate = new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero) }).Should().BeFalse();
    }

    [Fact]
    public void DateOnlyBeforeShouldExcludeSameDayOnDateTimeOffset()
    {
        var group = SingleCriterion("OffsetDueDate", FilterOperator.Before, new DateOnly(2026, 6, 15));

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { OffsetDueDate = new DateTimeOffset(2026, 6, 14, 23, 59, 59, TimeSpan.Zero) }).Should().BeTrue();
        predicate(new TestEntity { OffsetDueDate = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero) }).Should().BeFalse();
    }

    [Fact]
    public void DateOnlyAfterShouldExcludeSameDayOnDateTimeOffset()
    {
        var group = SingleCriterion("OffsetDueDate", FilterOperator.After, new DateOnly(2026, 6, 15));

        var predicate = _builder.Build(group).Compile();

        predicate(new TestEntity { OffsetDueDate = new DateTimeOffset(2026, 6, 16, 0, 0, 1, TimeSpan.Zero) }).Should().BeTrue();
        predicate(new TestEntity { OffsetDueDate = new DateTimeOffset(2026, 6, 15, 23, 59, 59, TimeSpan.Zero) }).Should().BeFalse();
    }

    // ── DF-11: defensive throws ──────────────────────────────────────────
    [Fact]
    public void DateOnlyEqualsShouldThrowWhenValueIsNull()
    {
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("DueDate", FilterOperator.Equals, null, new DateOnly(2026, 6, 15)),
        });

        Action act = () => _builder.Build(group);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DateOnlyBetweenShouldThrowWhenValueEndIsNull()
    {
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("DueDate", FilterOperator.Between, new DateOnly(2026, 6, 10), null),
        });

        Action act = () => _builder.Build(group);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DateOnlyNotBetweenShouldThrowWhenValueIsNull()
    {
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("DueDate", FilterOperator.NotBetween, null, new DateOnly(2026, 6, 15)),
        });

        Action act = () => _builder.Build(group);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DateOnlyWithUnsupportedOperatorShouldThrow()
    {
        var group = SingleCriterion("DueDate", FilterOperator.Contains, new DateOnly(2026, 6, 15));

        Action act = () => _builder.Build(group);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static FilterGroup SingleCriterion(string field, FilterOperator op, object? value)
    {
        return new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new(field, op, value),
        });
    }

    private sealed class TestEntity
    {
        public string Name { get; init; } = string.Empty;

        public decimal Amount { get; init; }

        public DateTime? DueDate { get; init; }

        public DateTimeOffset? OffsetDueDate { get; init; }

        public RelatedEntity Customer { get; init; } = new();
    }

    private sealed class RelatedEntity
    {
        public string City { get; init; } = string.Empty;
    }
}
