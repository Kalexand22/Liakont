namespace Liakont.Modules.Mandats.Infrastructure;

using System.Text.Json;
using Liakont.Modules.Mandats.Application;
using Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Construit les entrées du journal append-only des modifications de mandants/mandats (INV-MANDATS-3) à
/// partir du contexte d'une mutation et de l'identité de l'opérateur. Sérialise les valeurs avant/après
/// en JSON. La persistance atomique (mutation + entrée dans la même transaction) est assurée par
/// <c>IMandatsUnitOfWork</c>.
/// </summary>
internal static class MandatChangeLogFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static MandatChangeLogEntry ForCreateMandant(Mandant mandant, Guid operatorId, string? operatorName)
        => new()
        {
            CompanyId = mandant.CompanyId,
            MandantId = mandant.Id,
            MandatId = null,
            Reference = mandant.Reference,
            ChangeType = MandatChangeType.CreateMandant,
            BeforeJson = null,
            AfterJson = SerializeMandant(mandant),
            OperatorId = operatorId,
            OperatorName = operatorName,
        };

    public static MandatChangeLogEntry ForUpdateMandant(Mandant before, Mandant after, Guid operatorId, string? operatorName)
        => new()
        {
            CompanyId = after.CompanyId,
            MandantId = after.Id,
            MandatId = null,
            Reference = after.Reference,
            ChangeType = MandatChangeType.UpdateMandant,
            BeforeJson = SerializeMandant(before),
            AfterJson = SerializeMandant(after),
            OperatorId = operatorId,
            OperatorName = operatorName,
        };

    public static MandatChangeLogEntry ForCreateMandat(Mandat mandat, Guid operatorId, string? operatorName)
        => Base(mandat, MandatChangeType.CreateMandat, operatorId, operatorName) with
        {
            BeforeJson = null,
            AfterJson = SerializeMandat(mandat),
        };

    public static MandatChangeLogEntry ForUpdateMandat(Mandat before, Mandat after, Guid operatorId, string? operatorName)
        => Base(after, MandatChangeType.UpdateMandat, operatorId, operatorName) with
        {
            BeforeJson = SerializeMandat(before),
            AfterJson = SerializeMandat(after),
        };

    public static MandatChangeLogEntry ForValidateMandat(
        Mandat mandat, string? previousValidatedBy, DateOnly? previousValidatedDate, Guid operatorId, string? operatorName)
        => Base(mandat, MandatChangeType.ValidateMandat, operatorId, operatorName) with
        {
            BeforeJson = SerializeValidation(previousValidatedBy, previousValidatedDate),
            AfterJson = SerializeValidation(mandat.ValidatedBy, mandat.ValidatedDate),
        };

    public static MandatChangeLogEntry ForRevokeMandat(Mandat mandat, Guid operatorId, string? operatorName)
        => Base(mandat, MandatChangeType.RevokeMandat, operatorId, operatorName) with
        {
            BeforeJson = null,
            AfterJson = SerializeRevocation(mandat),
        };

    private static MandatChangeLogEntry Base(Mandat mandat, MandatChangeType changeType, Guid operatorId, string? operatorName)
        => new()
        {
            CompanyId = mandat.CompanyId,
            MandantId = mandat.MandantId,
            MandatId = mandat.Id,
            Reference = mandat.Reference,
            ChangeType = changeType,
            OperatorId = operatorId,
            OperatorName = operatorName,
        };

    private static string SerializeMandant(Mandant mandant)
        => JsonSerializer.Serialize(
            new
            {
                mandant.Reference,
                mandant.RaisonSociale,
                mandant.SellerVatNumber,
                mandant.Siren,
                mandant.NumberingPrefix,
            },
            SerializerOptions);

    private static string SerializeMandat(Mandat mandat)
        => JsonSerializer.Serialize(
            new
            {
                mandat.Reference,
                mandat.ClauseText,
                mandat.EstEcrit,
                mandat.AssujettissementStatus,
                ContestationDelay = mandat.ContestationDelay?.ToString(),
                mandat.IsValidated,
                mandat.IsRevoked,
                mandat.IsSelfBillingSuspended,
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

    private static string SerializeRevocation(Mandat mandat)
        => JsonSerializer.Serialize(
            new
            {
                RevokedDate = mandat.RevokedDate?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                mandat.IsSelfBillingSuspended,
            },
            SerializerOptions);
}
