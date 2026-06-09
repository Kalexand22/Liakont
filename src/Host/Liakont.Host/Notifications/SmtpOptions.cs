namespace Liakont.Host.Notifications;

/// <summary>
/// Configuration SMTP de NIVEAU INSTANCE (F12 §6.1, ADR-0018) liée depuis la section <c>Smtp</c> des
/// appsettings. Le mot de passe n'est JAMAIS versionné en clair (gabarit vide dans <c>appsettings.json</c> ;
/// valeur réelle par variable d'environnement / <c>appsettings.Production.json</c> non versionné —
/// CLAUDE.md n°10) ni journalisé (n°18).
/// </summary>
internal sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>Active l'envoi SMTP réel. Faux (défaut) : le transport journalise et n'envoie pas (no-op).</summary>
    public bool Enabled { get; init; }

    /// <summary>Hôte du serveur SMTP. Vide = transport inactif (no-op journalisé, pas d'exception).</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>Port SMTP (587 STARTTLS par défaut).</summary>
    public int Port { get; init; } = 587;

    /// <summary>Utilise STARTTLS (défaut) plutôt qu'une connexion SSL implicite.</summary>
    public bool UseStartTls { get; init; } = true;

    /// <summary>Identifiant d'authentification SMTP (vide = pas d'authentification).</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Mot de passe SMTP — JAMAIS versionné en clair ni journalisé (CLAUDE.md n°10/18).</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Adresse d'expéditeur (From) des emails d'alerte.</summary>
    public string FromAddress { get; init; } = string.Empty;

    /// <summary>Nom affiché de l'expéditeur.</summary>
    public string FromName { get; init; } = "Liakont";
}
