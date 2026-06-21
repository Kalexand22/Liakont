// Liakont addition (§4.36 lecture timestamptz) - not part of the original Stratum vendoring.
namespace Stratum.Common.Infrastructure.Database;

/// <summary>
/// Conversions robustes des horodatages lus via Dapper (lignes dynamiques) vers
/// <see cref="DateTimeOffset"/>. Npgsql renvoie un <see cref="DateTime"/> (Kind=Utc) pour une colonne
/// <c>timestamptz</c> : un cast direct <c>(DateTimeOffset)row.x</c> lève alors une
/// <see cref="InvalidCastException"/>. Ce convertisseur est indépendant de la représentation CLR exacte
/// renvoyée par Npgsql (DateTime UTC ou DateTimeOffset selon configuration) — même contrat que les
/// RowReader des modules Liakont (ex. TenantSettings), généralisé au socle.
/// </summary>
public static class DbTimestamp
{
    /// <summary>Convertit une valeur d'horodatage non nulle lue en base en <see cref="DateTimeOffset"/> (UTC).</summary>
    public static DateTimeOffset ToDateTimeOffset(object value)
    {
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidCastException(
                $"Type d'horodatage inattendu lu en base : {value?.GetType().FullName ?? "null"}."),
        };
    }

    /// <summary>Variante nullable : <c>null</c> et <see cref="DBNull"/> donnent <c>null</c>.</summary>
    public static DateTimeOffset? ToNullableDateTimeOffset(object? value)
    {
        return value is null or DBNull ? null : ToDateTimeOffset(value);
    }
}
