namespace Liakont.Host.Startup;

/// <summary>
/// Identité légale FICTIVE du tenant de DÉVELOPPEMENT (section <c>DevTenantSeed:Profile</c>,
/// appsettings.Development.json), saisie PROGRAMMATIQUEMENT au boot à froid pour rendre le pipeline de dev
/// fonctionnel (un profil est requis : <c>GetCurrentCompanyId()</c> en dépend — CFG02). L'identité n'est
/// JAMAIS dans le fichier de seed (BUG-14) ; la place des valeurs d'exemple est la configuration de dev,
/// jamais le code (CLAUDE.md n°7). Toutes les valeurs sont fictives (aucune donnée client).
/// </summary>
internal sealed class DevTenantProfileOptions
{
    /// <summary>SIREN fictif du tenant de dev (9 chiffres valides Luhn). Vide = profil de dev non amorcé.</summary>
    public string Siren { get; init; } = string.Empty;

    public string RaisonSociale { get; init; } = string.Empty;

    public string Street { get; init; } = string.Empty;

    public string PostalCode { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string Country { get; init; } = "FR";

    /// <summary>E-mail de contact d'alerte fictif, ou vide.</summary>
    public string? ContactEmailAlerte { get; init; }
}
