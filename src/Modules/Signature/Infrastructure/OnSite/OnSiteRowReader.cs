namespace Liakont.Modules.Signature.Infrastructure.OnSite;

using System;

/// <summary>
/// Conversions robustes des valeurs lues via Dapper (lignes dynamiques), indépendantes de la représentation
/// CLR exacte que Npgsql renvoie pour <c>timestamptz</c> (<c>DateTime</c> Kind=Utc ou <c>DateTimeOffset</c>
/// selon la configuration). Même patron que <c>DocumentApprovalRowReader</c>.
/// </summary>
internal static class OnSiteRowReader
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
}
