namespace Liakont.Host.Security.Abstractions;

using System.Collections.Generic;

/// <summary>Une ligne d'utilisateur de tenant pour la liste console (RB4).</summary>
public sealed record TenantUserLine
{
    /// <summary>Identifiant du compte chez le fournisseur d'identité (sub Keycloak).</summary>
    public required string IdpUserId { get; init; }

    /// <summary>Nom d'utilisateur (login).</summary>
    public required string Username { get; init; }

    /// <summary>Adresse e-mail.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Nom affiché (prénom + nom si présents, sinon nom d'utilisateur).</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Compte actif (peut se connecter) ou désactivé.</summary>
    public bool Enabled { get; init; }

    /// <summary>Rôles realm Liakont portés (lecture/operateur/parametrage/superviseur).</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
}
