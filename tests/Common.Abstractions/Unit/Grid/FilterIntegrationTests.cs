namespace Stratum.Common.Abstractions.Tests.Unit.Grid;

using FluentAssertions;
using Stratum.Common.Abstractions.Grid;
using Xunit;

/// <summary>
/// Integration-style tests: build a FilterGroup, compile it via FilterExpressionBuilder,
/// and verify it produces correct query results against a realistic in-memory dataset.
/// </summary>
public sealed class FilterIntegrationTests
{
    private static readonly string[] InCustomerNames = { "Acme Corp", "Delta Inc" };

    private static readonly List<InvoiceDto> TestData =
    [
        new() { Id = 1, InvoiceNumber = "INV-001", CustomerName = "Acme Corp", Amount = 1500m, DueDate = new DateTime(2025, 6, 15), IsPaid = false, Customer = new CustomerInfo { City = "Paris", Country = "France" } },
        new() { Id = 2, InvoiceNumber = "INV-002", CustomerName = "Acme Corp", Amount = 3200m, DueDate = new DateTime(2025, 7, 1), IsPaid = true, Customer = new CustomerInfo { City = "Paris", Country = "France" } },
        new() { Id = 3, InvoiceNumber = "INV-003", CustomerName = "Beta Ltd", Amount = 750m, DueDate = new DateTime(2025, 5, 20), IsPaid = false, Customer = new CustomerInfo { City = "London", Country = "UK" } },
        new() { Id = 4, InvoiceNumber = "INV-004", CustomerName = "Gamma SA", Amount = 5000m, DueDate = null, IsPaid = true, Customer = new CustomerInfo { City = "Lyon", Country = "France" } },
        new() { Id = 5, InvoiceNumber = "INV-005", CustomerName = "Delta Inc", Amount = 200m, DueDate = new DateTime(2025, 8, 10), IsPaid = false, Customer = new CustomerInfo { City = "New York", Country = "USA" } },
        new() { Id = 6, InvoiceNumber = "INV-006", CustomerName = "Acme Corp", Amount = 4500m, DueDate = new DateTime(2025, 6, 30), IsPaid = false, Customer = new CustomerInfo { City = "Paris", Country = "France" } },
    ];

    private readonly FilterExpressionBuilder<InvoiceDto> _builder = new();

    [Fact]
    public void SimpleTextFilterShouldReturnMatchingRecords()
    {
        var filter = new FilterGroup(FilterLogic.And, [
            new FilterCriterion("CustomerName", FilterOperator.Contains, "Acme"),
        ]);

        var results = Query(filter);

        results.Should().HaveCount(3);
        results.Should().OnlyContain(x => x.CustomerName == "Acme Corp");
    }

    [Fact]
    public void NumericRangeFilterShouldReturnMatchingRecords()
    {
        var filter = new FilterGroup(FilterLogic.And, [
            new FilterCriterion("Amount", FilterOperator.Between, 1000m, 4000m),
        ]);

        var results = Query(filter);

        results.Should().HaveCount(2);
        results.Select(x => x.Id).Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public void CombinedAndFilterShouldIntersectCriteria()
    {
        var filter = new FilterGroup(FilterLogic.And, [
            new FilterCriterion("CustomerName", FilterOperator.Contains, "Acme"),
            new FilterCriterion("IsPaid", FilterOperator.Equals, false),
        ]);

        var results = Query(filter);

        results.Should().HaveCount(2);
        results.Select(x => x.Id).Should().BeEquivalentTo([1, 6]);
    }

    [Fact]
    public void CombinedOrFilterShouldUnionCriteria()
    {
        var filter = new FilterGroup(FilterLogic.Or, [
            new FilterCriterion("Amount", FilterOperator.LessThan, 500m),
            new FilterCriterion("Amount", FilterOperator.GreaterThan, 4000m),
        ]);

        var results = Query(filter);

        results.Should().HaveCount(3);
        results.Select(x => x.Id).Should().BeEquivalentTo([4, 5, 6]);
    }

    [Fact]
    public void NestedGroupsShouldProduceCorrectResults()
    {
        var inner = new FilterGroup(FilterLogic.Or, [
            new FilterCriterion("Amount", FilterOperator.GreaterThan, 2000m),
            new FilterCriterion("IsPaid", FilterOperator.Equals, true),
        ]);
        var outer = new FilterGroup(
            FilterLogic.And,
            [new FilterCriterion("CustomerName", FilterOperator.Contains, "Acme")],
            [inner]);

        var results = Query(outer);

        results.Should().HaveCount(2);
        results.Select(x => x.Id).Should().BeEquivalentTo([2, 6]);
    }

    [Fact]
    public void RelatedTableFilterShouldResolveCorrectly()
    {
        var filter = new FilterGroup(FilterLogic.And, [
            new FilterCriterion("Customer.Country", FilterOperator.Equals, "France"),
        ]);

        var results = Query(filter);

        results.Should().HaveCount(4);
        results.Select(x => x.Id).Should().BeEquivalentTo([1, 2, 4, 6]);
    }

    [Fact]
    public void IsNullFilterShouldMatchNullDueDates()
    {
        var filter = new FilterGroup(FilterLogic.And, [
            new FilterCriterion("DueDate", FilterOperator.IsNull, null),
        ]);

        var results = Query(filter);

        results.Should().HaveCount(1);
        results.Single().Id.Should().Be(4);
    }

    [Fact]
    public void InOperatorShouldMatchMultipleValues()
    {
        var filter = new FilterGroup(FilterLogic.And, [
            new FilterCriterion("CustomerName", FilterOperator.In, InCustomerNames),
        ]);

        var results = Query(filter);

        results.Should().HaveCount(4);
        results.Select(x => x.Id).Should().BeEquivalentTo([1, 2, 5, 6]);
    }

    [Fact]
    public void ComplexMultiLevelFilterShouldProduceCorrectResults()
    {
        var group1 = new FilterGroup(FilterLogic.And, [
            new FilterCriterion("Customer.City", FilterOperator.Equals, "Paris"),
            new FilterCriterion("Amount", FilterOperator.GreaterThan, 2999m),
        ]);
        var group2 = new FilterGroup(FilterLogic.And, [
            new FilterCriterion("IsPaid", FilterOperator.Equals, false),
            new FilterCriterion("DueDate", FilterOperator.IsNotNull, null),
            new FilterCriterion("Amount", FilterOperator.LessThan, 1000m),
        ]);

        var outer = new FilterGroup(FilterLogic.Or, [], [group1, group2]);

        var results = Query(outer);

        results.Should().HaveCount(4);
        results.Select(x => x.Id).Should().BeEquivalentTo([2, 3, 5, 6]);
    }

    [Fact]
    public void EmptyFilterShouldReturnAllRecords()
    {
        var filter = new FilterGroup(FilterLogic.And, []);

        var results = Query(filter);

        results.Should().HaveCount(TestData.Count);
    }

    private List<InvoiceDto> Query(FilterGroup filter)
    {
        var predicate = _builder.Build(filter).Compile();
        return TestData.Where(predicate).ToList();
    }

    private sealed class InvoiceDto
    {
        public int Id { get; init; }

        public string InvoiceNumber { get; init; } = string.Empty;

        public string CustomerName { get; init; } = string.Empty;

        public decimal Amount { get; init; }

        public DateTime? DueDate { get; init; }

        public bool IsPaid { get; init; }

        public CustomerInfo Customer { get; init; } = new();
    }

    private sealed class CustomerInfo
    {
        public string City { get; init; } = string.Empty;

        public string Country { get; init; } = string.Empty;
    }
}
