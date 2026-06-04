namespace Liakont.Modules.Documents.Infrastructure;

using System;

/// <summary>
/// Conversions robustes des valeurs lues via Dapper (lignes dynamiques) — indépendantes de la
/// représentation CLR exacte que Npgsql renvoie pour <c>timestamptz</c> / <c>date</c>. Les colonnes
/// textuelles <c>state</c> / <c>event_type</c> sont restituées telles quelles dans les DTO de lecture
/// (fidélité à la base, pas de reconstitution d'agrégat sur le chemin de lecture).
/// </summary>
internal static class DocumentRowReader
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

    public static DateOnly ToDateOnly(object value)
    {
        return value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => throw new InvalidCastException(
                $"Type de date inattendu lu en base : {value.GetType().FullName}."),
        };
    }
}
