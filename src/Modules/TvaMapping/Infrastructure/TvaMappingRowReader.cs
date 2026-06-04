namespace Liakont.Modules.TvaMapping.Infrastructure;

using System.Text.Json;

/// <summary>
/// Conversions robustes des valeurs lues via Dapper (lignes dynamiques) — indépendantes de la
/// représentation CLR exacte que Npgsql renvoie pour <c>timestamptz</c> / <c>date</c> / <c>jsonb</c>.
/// </summary>
internal static class TvaMappingRowReader
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

    public static IReadOnlyDictionary<string, string>? ToSourceFlags(object? value)
    {
        return value switch
        {
            null => null,
            string json => DeserializeFlags(json),
            _ => throw new InvalidCastException(
                $"Type de flags source inattendu lu en base : {value.GetType().FullName}."),
        };
    }

    private static Dictionary<string, string>? DeserializeFlags(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return map is { Count: > 0 } ? map : null;
    }
}
