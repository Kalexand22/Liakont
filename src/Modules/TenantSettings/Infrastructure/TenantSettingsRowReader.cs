namespace Liakont.Modules.TenantSettings.Infrastructure;

/// <summary>
/// Conversions robustes des valeurs lues via Dapper (lignes dynamiques). Indépendant de la
/// représentation CLR exacte que Npgsql renvoie pour <c>timestamptz</c> (DateTime UTC ou
/// DateTimeOffset selon la configuration) — évite tout <c>InvalidCastException</c>.
/// </summary>
internal static class TenantSettingsRowReader
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

    public static IReadOnlyList<string> ToStringList(object? value)
    {
        return value switch
        {
            null => [],
            string[] array => array,
            IEnumerable<string> seq => seq.ToList(),
            _ => throw new InvalidCastException(
                $"Type de tableau inattendu lu en base : {value.GetType().FullName}."),
        };
    }
}
