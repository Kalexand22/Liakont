namespace Liakont.Host.Security.Abstractions;

using System;

/// <summary>Cause TYPÉE d'un échec de provisioning — le code HTTP s'y mappe, jamais au message français.</summary>
public enum TenantUserProvisionFailureReason
{
    /// <summary>Pas d'échec.</summary>
    None = 0,

    /// <summary>Demande invalide (rôle inconnu, champ manquant, IdP non configuré, company_id introuvable…).</summary>
    Invalid = 1,

    /// <summary>Tenant cible introuvable au registre.</summary>
    TenantNotFound = 2,

    /// <summary>Le nom d'utilisateur ou l'email existe déjà (pré-contrôle ou conflit IdP).</summary>
    Conflict = 3,
}

/// <summary>Résultat du provisioning d'un utilisateur de tenant (<see cref="ITenantUserProvisioningService"/>).</summary>
public sealed record TenantUserProvisionResult
{
    public required bool Success { get; init; }

    /// <summary>Cause typée de l'échec (<see cref="TenantUserProvisionFailureReason.None"/> en succès).</summary>
    public TenantUserProvisionFailureReason FailureReason { get; init; }

    /// <summary>Identifiant applicatif (<c>identity.users.id</c> de la base tenant = attribut <c>stratum_user_id</c>).</summary>
    public Guid? UserId { get; init; }

    /// <summary>Identifiant du compte chez le fournisseur d'identité (le futur <c>sub</c> OIDC).</summary>
    public string? IdpUserId { get; init; }

    /// <summary>Une invitation email a été ENVOYÉE (synchrone — jamais mise en file : le mot de passe ne se persiste pas).</summary>
    public bool InvitationEmailSent { get; init; }

    /// <summary>
    /// Mot de passe temporaire, renseigné UNIQUEMENT quand aucune invitation email n'est partie
    /// (SMTP non configuré ou envoi en échec) : à remettre à l'opérateur UNE SEULE FOIS (pattern clé
    /// API WEB09) — jamais persisté, jamais journalisé. L'IdP force le changement à la première connexion.
    /// </summary>
    public string? TemporaryPassword { get; init; }

    /// <summary>Message opérateur en français en cas d'échec (action corrective incluse).</summary>
    public string? ErrorMessage { get; init; }

    public static TenantUserProvisionResult Failed(
        string message,
        TenantUserProvisionFailureReason reason = TenantUserProvisionFailureReason.Invalid) =>
        new() { Success = false, ErrorMessage = message, FailureReason = reason };
}
