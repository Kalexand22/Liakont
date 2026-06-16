namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Identifiants SMTP propres au tenant pour la livraison par email (F16 §6.2 — « mot de passe SMTP
/// chiffré par tenant »). Optionnels : absents, l'implémentation de canal Host réutilise la connexion
/// SMTP de NIVEAU INSTANCE (ADR-0018). Le mot de passe est DÉJÀ DÉCHIFFRÉ (résolu Host-side via le
/// coffre du tenant, jamais porté en clair par le descripteur — CLAUDE.md n°10) et ne vit qu'en mémoire.
/// <para>
/// Type <c>class</c> volontairement (et NON <c>record</c>) : un <c>record</c> imprime tous ses membres
/// dans <c>ToString()</c> — un secret journalisé par mégarde serait un P1 (CLAUDE.md n°18). Cette classe
/// n'expose aucun rendu textuel de ses membres ; aucun log ne reçoit le mot de passe.
/// </para>
/// </summary>
public sealed class SmtpDeliveryAuthentication
{
    /// <summary>Hôte SMTP du tenant.</summary>
    public required string Host { get; init; }

    /// <summary>Port SMTP (587 STARTTLS par défaut).</summary>
    public int Port { get; init; } = 587;

    /// <summary>STARTTLS (défaut) plutôt qu'une connexion SSL implicite.</summary>
    public bool UseStartTls { get; init; } = true;

    /// <summary>Identifiant d'authentification SMTP (vide = pas d'authentification).</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Mot de passe SMTP DÉJÀ DÉCHIFFRÉ — en mémoire uniquement, jamais journalisé (CLAUDE.md n°10/18).</summary>
    public string Password { get; init; } = string.Empty;
}
