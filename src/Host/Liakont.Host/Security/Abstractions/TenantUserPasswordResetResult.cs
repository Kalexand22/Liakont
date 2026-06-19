namespace Liakont.Host.Security.Abstractions;

/// <summary>Résultat d'une réinitialisation de mot de passe d'un utilisateur de tenant (RB4).</summary>
public sealed record TenantUserPasswordResetResult
{
    /// <summary>La réinitialisation a réussi.</summary>
    public bool Success { get; init; }

    /// <summary>Message opérateur en français en cas d'échec.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// Mot de passe temporaire, remis UNE SEULE FOIS à l'opérateur quand aucune invitation email n'a pu
    /// être envoyée (SMTP non configuré ou envoi en échec). <c>null</c> si l'email est parti.
    /// </summary>
    public string? TemporaryPassword { get; init; }

    /// <summary>Une invitation a été envoyée par email (le mot de passe n'est alors restitué nulle part).</summary>
    public bool InvitationEmailSent { get; init; }

    /// <summary>Échec porteur d'un message opérateur en français.</summary>
    public static TenantUserPasswordResetResult Failed(string error) => new() { Success = false, Error = error };
}
