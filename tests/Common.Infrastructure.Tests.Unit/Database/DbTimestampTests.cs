namespace Stratum.Common.Infrastructure.Tests.Unit.Database;

using System;
using Stratum.Common.Infrastructure.Database;
using Xunit;

/// <summary>
/// Garde anti-régression du bug de recette RB (provenance §4.36) : Npgsql renvoie un
/// <see cref="DateTime"/> (Kind=Utc) pour une colonne <c>timestamptz</c> ; un cast direct
/// <c>(DateTimeOffset)value</c> lève <see cref="InvalidCastException"/>. <see cref="DbTimestamp"/>
/// doit convertir sans lever.
/// </summary>
public sealed class DbTimestampTests
{
    [Fact]
    public void ToDateTimeOffset_Should_ConvertToUtcOffset_When_NpgsqlReturnsUtcDateTime()
    {
        var utc = new DateTime(2026, 6, 18, 14, 30, 0, DateTimeKind.Utc);

        DateTimeOffset result = DbTimestamp.ToDateTimeOffset(utc);

        Assert.Equal(TimeSpan.Zero, result.Offset);
        Assert.Equal(utc, result.UtcDateTime);
    }

    [Fact]
    public void ToDateTimeOffset_Should_TreatUnspecifiedKindAsUtc_When_DateTimeHasNoKind()
    {
        var unspecified = new DateTime(2026, 6, 18, 14, 30, 0, DateTimeKind.Unspecified);

        DateTimeOffset result = DbTimestamp.ToDateTimeOffset(unspecified);

        Assert.Equal(TimeSpan.Zero, result.Offset);
        Assert.Equal(14, result.Hour);
    }

    [Fact]
    public void ToDateTimeOffset_Should_ReturnSameValue_When_AlreadyDateTimeOffset()
    {
        var dto = new DateTimeOffset(2026, 6, 18, 14, 30, 0, TimeSpan.FromHours(2));

        DateTimeOffset result = DbTimestamp.ToDateTimeOffset(dto);

        Assert.Equal(dto, result);
    }

    [Fact]
    public void ToDateTimeOffset_Should_Throw_When_ValueIsNotATimestamp()
    {
        Assert.Throws<InvalidCastException>(() => DbTimestamp.ToDateTimeOffset("2026-06-18"));
    }

    [Fact]
    public void ToNullableDateTimeOffset_Should_ReturnNull_When_ValueIsNull()
    {
        Assert.Null(DbTimestamp.ToNullableDateTimeOffset(null));
    }

    [Fact]
    public void ToNullableDateTimeOffset_Should_ReturnNull_When_ValueIsDbNull()
    {
        Assert.Null(DbTimestamp.ToNullableDateTimeOffset(DBNull.Value));
    }

    [Fact]
    public void ToNullableDateTimeOffset_Should_Convert_When_ValueIsUtcDateTime()
    {
        var utc = new DateTime(2026, 6, 18, 14, 30, 0, DateTimeKind.Utc);

        DateTimeOffset? result = DbTimestamp.ToNullableDateTimeOffset(utc);

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.Zero, result.Value.Offset);
        Assert.Equal(utc, result.Value.UtcDateTime);
    }
}
