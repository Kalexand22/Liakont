namespace Liakont.Host.Security;

using System;
using System.Collections.Generic;

/// <summary>
/// Sous-ensemble des permissions Liakont jugées <b>sensibles</b> sur un produit fiscal :
/// <see cref="LiakontPermissions.Actions"/> (actions opérateur dont l'envoi à l'administration) et
/// <see cref="LiakontPermissions.Settings"/> (paramétrage fiscal du tenant : table TVA, comptes PA).
/// </summary>
/// <remarks>
/// Source AUTORITATIVE — aucune valeur inventée : ces deux permissions sont nommées comme sensibles
/// par <c>docs/adr/ADR-0017-pont-role-permission-claims-oidc.md</c> (§Négatif) et l'item RDF10. La
/// fenêtre de révocation de ces permissions est <b>bornée</b> (cap de session court, sans glissement)
/// par <see cref="SensitivePermissionRevocation"/> — alors que les permissions non sensibles
/// (<see cref="LiakontPermissions.Read"/>, <see cref="LiakontPermissions.Supervision"/>,
/// <see cref="LiakontPermissions.Fleet"/>) restent sur la fenêtre glissante par défaut.
/// Membre <b>public</b> : la garde CI E2E (projet de tests E2E, hors visibilité des internes du Host)
/// itère dessus pour exiger ≥1 E2E par permission sensible avec un rôle non super-admin (trou IDN01).
/// </remarks>
public static class SensitivePermissions
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        LiakontPermissions.Actions,
        LiakontPermissions.Settings,
    };

    /// <summary>Permissions sensibles (ordre stable, insensible à la casse à la comparaison).</summary>
    public static IReadOnlyCollection<string> All { get; } =
    [
        LiakontPermissions.Actions,
        LiakontPermissions.Settings,
    ];

    /// <summary>Indique si la permission donnée est sensible (comparaison insensible à la casse).</summary>
    public static bool IsSensitive(string permission) =>
        permission is not null && Sensitive.Contains(permission);
}
