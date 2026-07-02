namespace Liakont.Modules.Ged.Infrastructure.Mapping;

using Liakont.Modules.Ged.Application.Mapping;
using Liakont.Modules.Ged.Domain.Mapping;

/// <summary>
/// Construit les entrées du journal APPEND-ONLY des profils de mapping GED (F19 §4.5 ; miroir de
/// <c>MappingChangeLogFactory</c> du domaine TVA). L'audit d'un profil est immuable : une naissance /
/// validation / mutation produit une NOUVELLE entrée, jamais un UPDATE (INV-GED-02, règle 4).
/// </summary>
internal static class GedMappingChangeLogFactory
{
    /// <summary>
    /// Entrée de CRÉATION d'un profil : <c>before</c> nul, <c>after</c> = instantané du profil (config gravée
    /// à sa naissance).
    /// </summary>
    /// <param name="profile">Le profil créé.</param>
    /// <param name="operatorIdentity">Identité de l'opérateur, ou <see langword="null"/>.</param>
    /// <param name="operatorName">Nom d'affichage de l'opérateur, ou <see langword="null"/>.</param>
    /// <returns>L'entrée de journal.</returns>
    public static GedMappingChangeLogEntry ForCreateProfile(
        GedMappingProfile profile,
        string? operatorIdentity,
        string? operatorName)
    {
        return new GedMappingChangeLogEntry
        {
            ChangeType = "profile_created",
            ProfileId = profile.Id,
            DocumentType = profile.DocumentType,
            ProfileVersion = profile.ProfileVersion,
            BeforeJson = null,
            AfterJson = Snapshot(profile),
            OperatorIdentity = operatorIdentity,
            OperatorName = operatorName,
        };
    }

    private static string Snapshot(GedMappingProfile profile)
    {
        return GedMappingProfileJson.SerializeValue(new
        {
            profile.DocumentType,
            profile.ProfileVersion,
            profile.StoragePolicy,
            IsValidated = profile.IsValidated,
            AxisRules = profile.AxisRules,
            EntityRules = profile.EntityRules,
            RelationRules = profile.RelationRules,
        });
    }
}
