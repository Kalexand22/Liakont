namespace Stratum.Common.Infrastructure.Tests.Unit.Grid;

using Dapper;
using FluentAssertions;
using Stratum.Common.Infrastructure.GridPreferences;
using Xunit;

public sealed class FullTextSearchSqlBuilderTests
{
    private static readonly Dictionary<string, string> FieldMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LegalName"] = "p.legal_name",
        ["TradeName"] = "p.trade_name",
        ["Status"] = "q.status",
        ["QuoteNumber"] = "q.quote_number",
        ["Party.LegalName"] = "p.legal_name",
        ["Party.TradeName"] = "p.trade_name",
    };

    [Fact]
    public void SingleColumnShouldProduceIlike()
    {
        var builder = new FullTextSearchSqlBuilder(FieldMap);
        var parameters = new DynamicParameters();

        var sql = builder.Build("Acme", ["LegalName"], parameters, out var skipped);

        sql.Should().Be("CAST(p.legal_name AS TEXT) ILIKE @fts ESCAPE '\\'");
        parameters.Get<string>("fts").Should().Be("%Acme%");
        skipped.Should().BeEmpty();
    }

    [Fact]
    public void MultipleColumnsShouldOrTogether()
    {
        var builder = new FullTextSearchSqlBuilder(FieldMap);
        var parameters = new DynamicParameters();

        var sql = builder.Build("test", ["LegalName", "TradeName", "Status"], parameters, out var skipped);

        sql.Should().Contain("CAST(p.legal_name AS TEXT) ILIKE @fts ESCAPE '\\'");
        sql.Should().Contain("CAST(p.trade_name AS TEXT) ILIKE @fts ESCAPE '\\'");
        sql.Should().Contain("CAST(q.status AS TEXT) ILIKE @fts ESCAPE '\\'");
        sql.Should().Contain(" OR ");
        skipped.Should().BeEmpty();
    }

    [Fact]
    public void CrossTableColumnsShouldResolve()
    {
        var builder = new FullTextSearchSqlBuilder(FieldMap);
        var parameters = new DynamicParameters();

        var sql = builder.Build("Acme", ["Party.LegalName", "Party.TradeName"], parameters, out var skipped);

        sql.Should().Contain("CAST(p.legal_name AS TEXT) ILIKE @fts");
        sql.Should().Contain("CAST(p.trade_name AS TEXT) ILIKE @fts");
        skipped.Should().BeEmpty();
    }

    [Fact]
    public void UnknownFieldShouldBeSkipped()
    {
        var builder = new FullTextSearchSqlBuilder(FieldMap);
        var parameters = new DynamicParameters();

        var sql = builder.Build("test", ["LegalName", "UnknownField"], parameters, out var skipped);

        sql.Should().Be("CAST(p.legal_name AS TEXT) ILIKE @fts ESCAPE '\\'");
        skipped.Should().ContainSingle().Which.Should().Be("UnknownField");
    }

    [Fact]
    public void AllFieldsUnknownShouldReturnNull()
    {
        var builder = new FullTextSearchSqlBuilder(FieldMap);
        var parameters = new DynamicParameters();

        var sql = builder.Build("test", ["Unknown1", "Unknown2"], parameters, out var skipped);

        sql.Should().BeNull();
        skipped.Should().HaveCount(2);
    }

    [Fact]
    public void EmptySearchTermShouldReturnNull()
    {
        var builder = new FullTextSearchSqlBuilder(FieldMap);
        var parameters = new DynamicParameters();

        var sql = builder.Build(string.Empty, ["LegalName"], parameters, out var skipped);

        sql.Should().BeNull();
    }

    [Fact]
    public void WhitespaceSearchTermShouldReturnNull()
    {
        var builder = new FullTextSearchSqlBuilder(FieldMap);
        var parameters = new DynamicParameters();

        var sql = builder.Build("   ", ["LegalName"], parameters, out _);

        sql.Should().BeNull();
    }

    [Fact]
    public void EmptyColumnListShouldReturnNull()
    {
        var builder = new FullTextSearchSqlBuilder(FieldMap);
        var parameters = new DynamicParameters();

        var sql = builder.Build("test", [], parameters, out _);

        sql.Should().BeNull();
    }

    [Fact]
    public void SpecialCharactersShouldBeEscaped()
    {
        var builder = new FullTextSearchSqlBuilder(FieldMap);
        var parameters = new DynamicParameters();

        builder.Build("100%_off\\deal", ["LegalName"], parameters, out _);

        parameters.Get<string>("fts").Should().Be("%100\\%\\_off\\\\deal%");
    }

    [Fact]
    public void NullParametersShouldThrow()
    {
        var builder = new FullTextSearchSqlBuilder(FieldMap);

        var act = () => builder.Build("test", ["LegalName"], null!, out _);

        act.Should().Throw<ArgumentNullException>();
    }
}
