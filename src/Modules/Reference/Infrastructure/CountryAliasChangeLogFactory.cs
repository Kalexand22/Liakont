namespace Liakont.Modules.Reference.Infrastructure;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Construit les entrées du journal append-only des mutations du référentiel de correspondance pays
/// (ADR-0038, §5) à partir de l'état avant/après et de l'identité de l'opérateur. Sérialise les valeurs
/// avant/après en JSON (clé nulle omise). La persistance atomique (mutation + entrée dans la MÊME
/// transaction) est assurée par <see cref="PostgresCountryAliasReferential"/>.
/// </summary>
internal static class CountryAliasChangeLogFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Entrée d'un ajout (avant nul → Create) ou d'une modification de cible (Update).</summary>
    public static CountryAliasChangeLogEntry ForUpsert(
        string sourceCode,
        string? beforeIsoCode,
        string afterIsoCode,
        Guid operatorId,
        string? operatorName)
        => new()
        {
            Id = Guid.NewGuid(),
            SourceCode = sourceCode,
            ChangeType = beforeIsoCode is null ? CountryAliasChangeType.Create : CountryAliasChangeType.Update,
            BeforeJson = beforeIsoCode is null ? null : SerializeAlias(beforeIsoCode),
            AfterJson = SerializeAlias(afterIsoCode),
            OperatorId = operatorId,
            OperatorName = operatorName,
            OccurredAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Entrée d'une suppression (après nul → Remove).</summary>
    public static CountryAliasChangeLogEntry ForRemove(
        string sourceCode,
        string beforeIsoCode,
        Guid operatorId,
        string? operatorName)
        => new()
        {
            Id = Guid.NewGuid(),
            SourceCode = sourceCode,
            ChangeType = CountryAliasChangeType.Remove,
            BeforeJson = SerializeAlias(beforeIsoCode),
            AfterJson = null,
            OperatorId = operatorId,
            OperatorName = operatorName,
            OccurredAt = DateTimeOffset.UtcNow,
        };

    private static string SerializeAlias(string isoCode)
        => JsonSerializer.Serialize(new { IsoCode = isoCode }, SerializerOptions);
}
