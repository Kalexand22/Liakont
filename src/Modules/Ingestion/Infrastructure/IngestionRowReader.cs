namespace Liakont.Modules.Ingestion.Infrastructure;

/// <summary>
/// Conversions robustes des valeurs lues via Dapper (lignes dynamiques). Indépendant de la
/// représentation CLR exacte que Npgsql renvoie pour <c>timestamptz</c> (DateTime UTC ou
/// DateTimeOffset selon la configuration) — évite tout <c>InvalidCastException</c>.
/// </summary>
internal static class IngestionRowReader
{
    public static DateTimeOffset ToDateTimeOffset(object value)
    {
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidCastException(
                $"Type d'horodatage inattendu lu en base : {value.GetType().FullName}."),
        };
    }

    public static DateTimeOffset? ToNullableDateTimeOffset(object? value)
    {
        return value is null ? null : ToDateTimeOffset(value);
    }
}
