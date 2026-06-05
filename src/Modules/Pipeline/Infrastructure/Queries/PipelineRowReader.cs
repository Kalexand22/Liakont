namespace Liakont.Modules.Pipeline.Infrastructure.Queries;

using System;

/// <summary>
/// Conversions robustes des valeurs lues via Dapper (lignes dynamiques), indépendantes de la
/// représentation CLR exacte que Npgsql renvoie pour <c>timestamptz</c>. Même motif que
/// <c>DocumentRowReader</c> / <c>TenantSettingsRowReader</c>.
/// </summary>
internal static class PipelineRowReader
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

    public static DateTimeOffset? ToNullableDateTimeOffset(object? value) =>
        value is null ? null : ToDateTimeOffset(value);
}
