namespace Stratum.Common.Infrastructure.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Infrastructure.GridPreferences;
using Xunit;

public sealed class FilterGroupSqlBuilderTests
{
    private static readonly string[] InStatusValues = { "open", "closed" };

    private static readonly Dictionary<string, string> FieldMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Name"] = "p.name",
        ["LegalName"] = "p.legal_name",
        ["Amount"] = "i.amount",
        ["DueDate"] = "i.due_date",
        ["IsActive"] = "p.is_active",
        ["Status"] = "i.status",
        ["Customer.City"] = "c.city",
    };

    [Fact]
    public void EqualsShouldProduceCorrectSql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("Name", FilterOperator.Equals, "Alice");

        var (sql, parameters) = builder.Build(group);

        sql.Should().Be("p.name = @af0");
        parameters.Get<string>("af0").Should().Be("Alice");
    }

    [Fact]
    public void NotEqualsShouldProduceCorrectSql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("Name", FilterOperator.NotEquals, "Bob");

        var (sql, _) = builder.Build(group);

        sql.Should().Be("p.name <> @af0");
    }

    [Fact]
    public void ContainsShouldProduceIlikeSql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("Name", FilterOperator.Contains, "test");

        var (sql, parameters) = builder.Build(group);

        sql.Should().Be(@"p.name ILIKE @af0 ESCAPE '\'");
        parameters.Get<string>("af0").Should().Be("%test%");
    }

    [Fact]
    public void StartsWithShouldProduceIlikeSql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("Name", FilterOperator.StartsWith, "Al");

        var (sql, parameters) = builder.Build(group);

        sql.Should().Be(@"p.name ILIKE @af0 ESCAPE '\'");
        parameters.Get<string>("af0").Should().Be("Al%");
    }

    [Fact]
    public void GreaterThanShouldProduceCorrectSql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("Amount", FilterOperator.GreaterThan, 100m);

        var (sql, _) = builder.Build(group);

        sql.Should().Be("i.amount > @af0");
    }

    [Fact]
    public void LessThanShouldProduceCorrectSql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("Amount", FilterOperator.LessThan, 50m);

        var (sql, _) = builder.Build(group);

        sql.Should().Be("i.amount < @af0");
    }

    [Fact]
    public void BetweenShouldProduceTwoParameterSql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var criterion = new FilterCriterion("Amount", FilterOperator.Between, 100m, 500m);
        var group = new FilterGroup(FilterLogic.And, [criterion]);

        var (sql, _) = builder.Build(group);

        sql.Should().Be("i.amount >= @af0 AND i.amount <= @af1");
    }

    [Fact]
    public void InShouldProduceArrayAnySql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var criterion = new FilterCriterion("Status", FilterOperator.In, InStatusValues);
        var group = new FilterGroup(FilterLogic.And, [criterion]);

        var (sql, _) = builder.Build(group);

        sql.Should().Be("i.status = ANY(@af0)");
    }

    [Fact]
    public void EmptyInShouldProduceAlwaysFalse()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var criterion = new FilterCriterion("Status", FilterOperator.In, Array.Empty<string>());
        var group = new FilterGroup(FilterLogic.And, [criterion]);

        var (sql, _) = builder.Build(group);

        sql.Should().Be("1 = 0");
    }

    [Fact]
    public void IsNullShouldProduceIsNullSql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("DueDate", FilterOperator.IsNull, null);

        var (sql, _) = builder.Build(group);

        sql.Should().Be("i.due_date IS NULL");
    }

    [Fact]
    public void IsNotNullShouldProduceIsNotNullSql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("DueDate", FilterOperator.IsNotNull, null);

        var (sql, _) = builder.Build(group);

        sql.Should().Be("i.due_date IS NOT NULL");
    }

    [Fact]
    public void AndLogicShouldJoinWithAnd()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "Alice"),
            new("IsActive", FilterOperator.Equals, true),
        });

        var (sql, _) = builder.Build(group);

        sql.Should().Be("p.name = @af0 AND p.is_active = @af1");
    }

    [Fact]
    public void OrLogicShouldJoinWithOr()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = new FilterGroup(FilterLogic.Or, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "Alice"),
            new("Name", FilterOperator.Equals, "Bob"),
        });

        var (sql, _) = builder.Build(group);

        sql.Should().Be("p.name = @af0 OR p.name = @af1");
    }

    [Fact]
    public void NestedGroupsShouldBeWrappedInParentheses()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var inner = new FilterGroup(FilterLogic.Or, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "Alice"),
            new("Name", FilterOperator.Equals, "Bob"),
        });
        var outer = new FilterGroup(
            FilterLogic.And,
            new List<FilterCriterion> { new("IsActive", FilterOperator.Equals, true) },
            new List<FilterGroup> { inner });

        var (sql, _) = builder.Build(outer);

        sql.Should().Be("p.is_active = @af0 AND (p.name = @af1 OR p.name = @af2)");
    }

    [Fact]
    public void UnknownFieldShouldBeSilentlySkipped()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "Alice"),
            new("HackerField", FilterOperator.Equals, "DROP TABLE;"),
        });

        var (sql, _) = builder.Build(group);

        sql.Should().Be("p.name = @af0");
        sql.Should().NotContain("HackerField");
        sql.Should().NotContain("DROP");
    }

    [Fact]
    public void EmptyGroupShouldReturnNull()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion>());

        var (sql, _) = builder.Build(group);

        sql.Should().BeNull();
    }

    [Fact]
    public void EmptyFieldShouldBeSkipped()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new(string.Empty, FilterOperator.Equals, "test"),
            new("Name", FilterOperator.Equals, "Alice"),
        });

        var (sql, _) = builder.Build(group);

        sql.Should().Be("p.name = @af0");
    }

    [Fact]
    public void ContainsShouldEscapeSpecialCharacters()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("Name", FilterOperator.Contains, "100%_done");

        var (_, parameters) = builder.Build(group);

        var param = parameters.Get<string>("af0");
        param.Should().Be(@"%100\%\_done%");
    }

    [Fact]
    public void ContainsShouldEscapeBackslash()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("Name", FilterOperator.Contains, @"path\to");

        var (_, parameters) = builder.Build(group);

        var param = parameters.Get<string>("af0");
        param.Should().Be(@"%path\\to%");
    }

    [Fact]
    public void ShouldResolveDotNotationFieldNames()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("Customer.City", FilterOperator.Equals, "Paris");

        var (sql, _) = builder.Build(group);

        sql.Should().Be("c.city = @af0");
    }

    [Fact]
    public void DateTimeOffsetShouldBeConvertedToUtcDateTime()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var dto = new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.FromHours(2));
        var group = SingleCriterion("DueDate", FilterOperator.Equals, dto);

        var (_, parameters) = builder.Build(group);

        parameters.Get<DateTime>("af0").Should().Be(dto.UtcDateTime);
    }

    [Fact]
    public void ParametersShouldBeSequentiallyIndexed()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = new FilterGroup(FilterLogic.And, new List<FilterCriterion>
        {
            new("Name", FilterOperator.Equals, "A"),
            new("Amount", FilterOperator.GreaterThan, 100m),
            new("IsActive", FilterOperator.Equals, true),
        });

        var (sql, _) = builder.Build(group);

        sql.Should().Contain("@af0").And.Contain("@af1").And.Contain("@af2");
    }

    [Fact]
    public void NotLikeShouldProduceNotIlikeSql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("Name", FilterOperator.NotContains, "test");

        var (sql, parameters) = builder.Build(group);

        sql.Should().Be(@"p.name NOT ILIKE @af0 ESCAPE '\'");
        parameters.Get<string>("af0").Should().Be("%test%");
    }

    [Fact]
    public void NotBetweenShouldProduceNotBetweenSql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var criterion = new FilterCriterion("Amount", FilterOperator.NotBetween, 100m, 500m);
        var group = new FilterGroup(FilterLogic.And, [criterion]);

        var (sql, _) = builder.Build(group);

        sql.Should().Be("NOT (i.amount >= @af0 AND i.amount <= @af1)");
    }

    [Fact]
    public void NotInShouldProduceNotAnySql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var criterion = new FilterCriterion("Status", FilterOperator.NotIn, InStatusValues);
        var group = new FilterGroup(FilterLogic.And, [criterion]);

        var (sql, _) = builder.Build(group);

        sql.Should().Be("NOT (i.status = ANY(@af0))");
    }

    [Fact]
    public void EmptyNotInShouldProduceAlwaysTrue()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var criterion = new FilterCriterion("Status", FilterOperator.NotIn, Array.Empty<string>());
        var group = new FilterGroup(FilterLogic.And, [criterion]);

        var (sql, _) = builder.Build(group);

        sql.Should().Be("1 = 1");
    }

    [Fact]
    public void RelativePeriodEnumShouldProduceBetweenSql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("DueDate", FilterOperator.RelativePeriod, RelativeDatePeriod.Today);

        var (sql, _) = builder.Build(group);

        sql.Should().Be("i.due_date >= @af0 AND i.due_date <= @af1");
    }

    [Fact]
    public void RelativePeriodStringShouldProduceBetweenSql()
    {
        var builder = new FilterGroupSqlBuilder(FieldMap);
        var group = SingleCriterion("DueDate", FilterOperator.RelativePeriod, "Today");

        var (sql, _) = builder.Build(group);

        sql.Should().Be("i.due_date >= @af0 AND i.due_date <= @af1");
    }

    private static FilterGroup SingleCriterion(string field, FilterOperator op, object? value)
    {
        return new FilterGroup(FilterLogic.And, [new FilterCriterion(field, op, value)]);
    }
}
