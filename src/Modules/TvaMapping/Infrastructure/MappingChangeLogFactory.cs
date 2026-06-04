namespace Liakont.Modules.TvaMapping.Infrastructure;

using System.Text.Json;
using Liakont.Modules.TvaMapping.Application;
using Liakont.Modules.TvaMapping.Domain.Entities;

/// <summary>
/// Construit les entrées du journal append-only des modifications de mapping TVA (item TVA05 §3) à
/// partir du contexte d'une mutation et de l'identité de l'opérateur. Sérialise les valeurs avant/après
/// en JSON (énumérations par leur nom — même convention que les DTO de lecture). La persistance atomique
/// (mutation + entrée dans la même transaction) est assurée par <c>ITvaMappingUnitOfWork.SaveMutationAsync</c>.
/// </summary>
internal static class MappingChangeLogFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static MappingChangeLogEntry ForAddRule(
        MappingTable table, MappingRule added, Guid operatorId, string? operatorName)
        => Base(table, MappingChangeType.AddRule, operatorId, operatorName) with
        {
            SourceRegimeCode = added.SourceRegimeCode,
            Part = added.Part,
            BeforeJson = null,
            AfterJson = SerializeRule(added),
        };

    public static MappingChangeLogEntry ForUpdateRule(
        MappingTable table, MappingRule before, MappingRule after, Guid operatorId, string? operatorName)
        => Base(table, MappingChangeType.UpdateRule, operatorId, operatorName) with
        {
            SourceRegimeCode = after.SourceRegimeCode,
            Part = after.Part,
            BeforeJson = SerializeRule(before),
            AfterJson = SerializeRule(after),
        };

    public static MappingChangeLogEntry ForRemoveRule(
        MappingTable table, MappingRule removed, Guid operatorId, string? operatorName)
        => Base(table, MappingChangeType.RemoveRule, operatorId, operatorName) with
        {
            SourceRegimeCode = removed.SourceRegimeCode,
            Part = removed.Part,
            BeforeJson = SerializeRule(removed),
            AfterJson = null,
        };

    public static MappingChangeLogEntry ForValidate(
        MappingTable table,
        string? previousValidatedBy,
        DateOnly? previousValidatedDate,
        Guid operatorId,
        string? operatorName)
        => Base(table, MappingChangeType.Validate, operatorId, operatorName) with
        {
            BeforeJson = SerializeValidation(previousValidatedBy, previousValidatedDate),
            AfterJson = SerializeValidation(table.ValidatedBy, table.ValidatedDate),
        };

    private static MappingChangeLogEntry Base(
        MappingTable table, MappingChangeType changeType, Guid operatorId, string? operatorName)
        => new()
        {
            CompanyId = table.CompanyId,
            TableId = table.Id,
            MappingVersion = table.MappingVersion,
            ChangeType = changeType,
            OperatorId = operatorId,
            OperatorName = operatorName,
        };

    private static string SerializeRule(MappingRule rule)
        => JsonSerializer.Serialize(
            new
            {
                rule.SourceRegimeCode,
                rule.Label,
                Part = rule.Part.ToString(),
                rule.SourceFlags,
                Category = rule.Category.ToString(),
                rule.Vatex,
                rule.Note,
                RateMode = rule.RateMode.ToString(),
                rule.RateValue,
            },
            SerializerOptions);

    private static string SerializeValidation(string? validatedBy, DateOnly? validatedDate)
        => JsonSerializer.Serialize(
            new
            {
                ValidatedBy = validatedBy,
                ValidatedDate = validatedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            },
            SerializerOptions);
}
