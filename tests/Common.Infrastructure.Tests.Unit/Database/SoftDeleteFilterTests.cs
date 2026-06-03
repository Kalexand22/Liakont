namespace Stratum.Common.Infrastructure.Tests.Unit.Database;

using System;
using Stratum.Common.Infrastructure.Database;
using Xunit;

public sealed class SoftDeleteFilterTests
{
    [Fact]
    public void NotDeleted_Should_ReturnDeletedAtIsNull_When_Accessed()
    {
        Assert.Equal("deleted_at IS NULL", SoftDeleteFilter.NotDeleted);
    }

    [Fact]
    public void WhereNotDeleted_Should_ReturnUnqualifiedPredicate_When_NoAliasProvided()
    {
        string result = SoftDeleteFilter.WhereNotDeleted();
        Assert.Equal("deleted_at IS NULL", result);
    }

    [Fact]
    public void WhereNotDeleted_Should_ReturnPrefixedPredicate_When_AliasProvided()
    {
        string result = SoftDeleteFilter.WhereNotDeleted("d");
        Assert.Equal("d.deleted_at IS NULL", result);
    }

    [Fact]
    public void WhereNotDeleted_Should_ReturnUnqualifiedPredicate_When_AliasIsNull()
    {
        string result = SoftDeleteFilter.WhereNotDeleted(null);
        Assert.Equal("deleted_at IS NULL", result);
    }

    [Fact]
    public void WhereNotDeleted_Should_ReturnUnqualifiedPredicate_When_AliasIsEmpty()
    {
        string result = SoftDeleteFilter.WhereNotDeleted(string.Empty);
        Assert.Equal("deleted_at IS NULL", result);
    }

    [Fact]
    public void WhereNotDeleted_Should_ThrowArgumentException_When_AliasContainsInvalidChars()
    {
        Assert.Throws<ArgumentException>(() => SoftDeleteFilter.WhereNotDeleted("d; DROP TABLE"));
    }

    [Fact]
    public void AndNotDeleted_Should_ReturnAndClause_When_NoAliasProvided()
    {
        string result = SoftDeleteFilter.AndNotDeleted();
        Assert.Equal(" AND deleted_at IS NULL", result);
    }

    [Fact]
    public void AndNotDeleted_Should_ReturnPrefixedAndClause_When_AliasProvided()
    {
        string result = SoftDeleteFilter.AndNotDeleted("t");
        Assert.Equal(" AND t.deleted_at IS NULL", result);
    }

    [Fact]
    public void AndNotDeleted_Should_ReturnUnqualifiedAndClause_When_AliasIsNull()
    {
        string result = SoftDeleteFilter.AndNotDeleted(null);
        Assert.Equal(" AND deleted_at IS NULL", result);
    }

    [Fact]
    public void AndNotDeleted_Should_ReturnValidSql_When_AppendedToWhereClause()
    {
        string baseSql = "SELECT * FROM document.documents WHERE id = @Id";
        string filtered = baseSql + SoftDeleteFilter.AndNotDeleted();

        Assert.Equal(
            "SELECT * FROM document.documents WHERE id = @Id AND deleted_at IS NULL",
            filtered);
    }

    [Fact]
    public void WhereNotDeleted_Should_ReturnValidSql_When_UsedAsOnlyCondition()
    {
        string sql = $"SELECT * FROM document.documents WHERE {SoftDeleteFilter.WhereNotDeleted()}";

        Assert.Equal(
            "SELECT * FROM document.documents WHERE deleted_at IS NULL",
            sql);
    }

    [Fact]
    public void AndNotDeleted_Should_ThrowArgumentException_When_AliasContainsInvalidChars()
    {
        Assert.Throws<ArgumentException>(() => SoftDeleteFilter.AndNotDeleted("bad alias"));
    }
}
