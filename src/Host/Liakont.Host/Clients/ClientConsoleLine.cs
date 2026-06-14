namespace Liakont.Host.Clients;

using System;

/// <summary>Statut métier AFFICHÉ d'un client sur l'écran Clients (OPS03) — jamais inventé : reflète l'état réel.</summary>
public enum ClientStatut
{
    /// <summary>Profil présent au statut « Actif ».</summary>
    Actif,

    /// <summary>Profil présent au statut « Suspendu » (OPS03.4 : push agent et connexions refusés).</summary>
    Suspendu,

    /// <summary>Tenant provisionné mais SANS profil (seed/saisie non faits) — état réel, pas une erreur.</summary>
    ProfilNonCree,

    /// <summary>Tenant désactivé au registre (fin de vie, realm supprimé) — visible, actions désactivées.</summary>
    Desactive,
}

/// <summary>
/// Ligne de l'écran « Clients » (OPS03) : registre système (id, nom, date) + profil du tenant lu dans
/// SON scope (SIREN, statut métier) + compteur d'agents (registre système scopé par tenantId).
/// <see cref="ReadFailed"/> : la lecture du profil a échoué — la ligne reste VISIBLE, jamais masquée.
/// </summary>
public sealed record ClientConsoleLine
{
    public required string TenantId { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>SIREN du profil, ou <c>null</c> (profil non créé / lecture en échec).</summary>
    public string? Siren { get; init; }

    public required ClientStatut Statut { get; init; }

    /// <summary>Nombre d'agents NON révoqués du tenant.</summary>
    public required int AgentCount { get; init; }

    public required DateTimeOffset ProvisionedAt { get; init; }

    /// <summary>La lecture du profil du tenant a échoué (base injoignable…) — signalé, jamais silencieux.</summary>
    public bool ReadFailed { get; init; }
}
