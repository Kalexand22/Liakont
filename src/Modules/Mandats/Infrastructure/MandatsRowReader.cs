namespace Liakont.Modules.Mandats.Infrastructure;

/// <summary>
/// Conversions robustes des valeurs lues via Dapper (lignes dynamiques) — indépendantes de la
/// représentation CLR exacte que Npgsql renvoie pour <c>timestamptz</c> / <c>date</c> / <c>interval</c>.
/// </summary>
internal static class MandatsRowReader
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

    public static DateOnly? ToNullableDateOnly(object? value)
    {
        return value switch
        {
            null => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => throw new InvalidCastException(
                $"Type de date inattendu lu en base : {value.GetType().FullName}."),
        };
    }

    public static TimeSpan? ToNullableTimeSpan(object? value)
    {
        return value switch
        {
            null => null,
            TimeSpan ts => ts,
            _ => throw new InvalidCastException(
                $"Type de durée (interval) inattendu lu en base : {value.GetType().FullName}."),
        };
    }
}
