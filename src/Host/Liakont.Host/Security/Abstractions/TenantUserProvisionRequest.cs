namespace Liakont.Host.Security.Abstractions;

/// <summary>Demande de provisioning d'un utilisateur de tenant (<see cref="ITenantUserProvisioningService"/>).</summary>
public sealed record TenantUserProvisionRequest
{
    /// <summary>Tenant cible (id du registre — le realm et la base en découlent).</summary>
    public required string TenantId { get; init; }

    /// <summary>Adresse email de l'utilisateur (unique dans le realm ; destinataire de l'invitation).</summary>
    public required string Email { get; init; }

    /// <summary>Nom d'utilisateur (unique dans le realm).</summary>
    public required string Username { get; init; }

    /// <summary>Nom affiché (console + audit).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Rôle realm standard (LiakontRealmRoles) : lecture | operateur | parametrage | superviseur.</summary>
    public required string Role { get; init; }
}
