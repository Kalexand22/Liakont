namespace Liakont.Host.Security;

using System.Collections.Generic;

/// <summary>
/// Rôles realm STANDARD d'un tenant Liakont (matrice rôle→permission de
/// docs/architecture/identity-permissions-liakont.md §3, projetée par
/// <see cref="RolePermissionCatalog"/>). Source unique des rôles proposables au provisioning
/// d'un utilisateur de tenant — aucun rôle inventé (CLAUDE.md n°2) ; un test verrouille
/// l'alignement avec le catalogue de permissions.
/// </summary>
internal static class LiakontRealmRoles
{
    /// <summary>Rôles standard, dans l'ordre croissant de privilèges, avec leur description française.</summary>
    public static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["lecture"] = "Consultation seule (documents, encaissements, traitements)",
            ["operateur"] = "Consultation + actions opérateur (envoi, re-vérification, résolution)",
            ["parametrage"] = "Opérateur + paramétrage du tenant (fiscal, table TVA, comptes PA)",
            ["superviseur"] = "Paramétrage + supervision de l'instance (cross-tenant, lecture seule)",
        };

    /// <summary>Noms des rôles standard.</summary>
    public static IReadOnlyCollection<string> All => Descriptions.Keys.ToList();

    /// <summary>Le rôle est un rôle realm standard Liakont.</summary>
    public static bool IsKnown(string role) => Descriptions.ContainsKey(role);
}
