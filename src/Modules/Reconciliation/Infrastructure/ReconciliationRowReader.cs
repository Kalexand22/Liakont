namespace Liakont.Modules.Reconciliation.Infrastructure;

using System;

/// <summary>
/// Conversions robustes des valeurs lues via Dapper (lignes dynamiques) — indépendantes de la
/// représentation CLR exacte que Npgsql renvoie pour <c>timestamptz</c> (même motif que
/// <c>DocumentRowReader</c> du module Documents).
/// </summary>
internal static class ReconciliationRowReader
{
    public static DateTimeOffset ToDateTimeOffset(object value)
    {
        return value switch
        {
            DateTimeOffset dto => dto.ToUniversalTime(),
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidCastException($"Type d'horodatage inattendu lu en base : {value.GetType().FullName}."),
        };
    }
}
