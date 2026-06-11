namespace Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Une entrée de la matrice de routage des alertes d'un tenant (F12 §5.3.1, FIX212). Route le
/// destinataire CÔTÉ TENANT d'une notification d'alerte par <see cref="RuleKey"/> (une règle F12 §5.2)
/// et/ou par <see cref="Severity"/> (gravité), vers une liste d'e-mails. EXTENSION du modèle simple :
/// quand le tenant n'a aucune entrée applicable, le côté tenant retombe sur le modèle simple (contact
/// critique opt-in) — la matrice n'enlève jamais ce repli (le routage côté Supervision). Paramétrage
/// produit : aucune règle fiscale, aucune donnée client embarquée.
/// </summary>
public sealed class AlertRoutingRule
{
    /// <summary>Jeton de gravité « avertissement » — miroir de F12 §5.2 (🟠) et du nom d'énumération Supervision.</summary>
    public const string SeverityWarning = "Warning";

    /// <summary>Jeton de gravité « critique » — miroir de F12 §5.2 (🔴) et du nom d'énumération Supervision.</summary>
    public const string SeverityCritical = "Critical";

    private AlertRoutingRule(
        Guid id,
        Guid companyId,
        string? ruleKey,
        string? severity,
        IReadOnlyList<string> recipients,
        int ordinal,
        DateTimeOffset createdAt)
    {
        Id = id;
        CompanyId = companyId;
        RuleKey = ruleKey;
        Severity = severity;
        Recipients = recipients;
        Ordinal = ordinal;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid CompanyId { get; }

    /// <summary>Règle ciblée (F12 §5.2), ou <c>null</c> pour « toute règle ».</summary>
    public string? RuleKey { get; }

    /// <summary>Gravité ciblée (<see cref="SeverityWarning"/>/<see cref="SeverityCritical"/>), ou <c>null</c> pour « toute gravité ».</summary>
    public string? Severity { get; }

    /// <summary>Destinataires e-mail (au moins un).</summary>
    public IReadOnlyList<string> Recipients { get; }

    /// <summary>Rang d'affichage/évaluation (stable, 0..N-1).</summary>
    public int Ordinal { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Crée une entrée de matrice. <paramref name="ruleKey"/> et <paramref name="severity"/> sont
    /// normalisés (trim, vide ⇒ <c>null</c>) ; au moins l'un des deux doit rester renseigné. La gravité,
    /// si présente, doit valoir <see cref="SeverityWarning"/> ou <see cref="SeverityCritical"/>. Les
    /// destinataires sont nettoyés (trim, doublons retirés en insensible à la casse) et doivent rester
    /// non vides, chacun ressemblant à un e-mail (INV-TENANTSETTINGS-011).
    /// </summary>
    public static AlertRoutingRule Create(
        Guid companyId,
        string? ruleKey,
        string? severity,
        IReadOnlyList<string> recipients,
        int ordinal)
    {
        return Build(Guid.NewGuid(), companyId, ruleKey, severity, recipients, ordinal, DateTimeOffset.UtcNow);
    }

    /// <summary>Reconstitue une entrée depuis la base (aucune validation : la donnée est déjà passée par <see cref="Create"/>).</summary>
    public static AlertRoutingRule Reconstitute(
        Guid id,
        Guid companyId,
        string? ruleKey,
        string? severity,
        IReadOnlyList<string> recipients,
        int ordinal,
        DateTimeOffset createdAt)
    {
        return new AlertRoutingRule(id, companyId, ruleKey, severity, recipients, ordinal, createdAt);
    }

    private static AlertRoutingRule Build(
        Guid id,
        Guid companyId,
        string? ruleKey,
        string? severity,
        IReadOnlyList<string> recipients,
        int ordinal,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(recipients);

        var normalizedRuleKey = Normalize(ruleKey);
        var normalizedSeverity = Normalize(severity);

        if (normalizedRuleKey is null && normalizedSeverity is null)
        {
            throw new ArgumentException(
                "INV-TENANTSETTINGS-011 : une entrée de routage doit cibler au moins une règle ou une gravité.",
                nameof(ruleKey));
        }

        if (normalizedSeverity is not null
            && !string.Equals(normalizedSeverity, SeverityWarning, StringComparison.Ordinal)
            && !string.Equals(normalizedSeverity, SeverityCritical, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"INV-TENANTSETTINGS-011 : gravité de routage inconnue « {normalizedSeverity} » (attendu {SeverityWarning} ou {SeverityCritical}).",
                nameof(severity));
        }

        var cleanedRecipients = NormalizeRecipients(recipients);
        if (cleanedRecipients.Count == 0)
        {
            throw new ArgumentException(
                "INV-TENANTSETTINGS-011 : une entrée de routage doit comporter au moins un destinataire e-mail.",
                nameof(recipients));
        }

        if (ordinal < 0)
        {
            throw new ArgumentException("INV-TENANTSETTINGS-011 : le rang d'une entrée de routage doit être positif ou nul.", nameof(ordinal));
        }

        return new AlertRoutingRule(id, companyId, normalizedRuleKey, normalizedSeverity, cleanedRecipients, ordinal, createdAt);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static List<string> NormalizeRecipients(IReadOnlyList<string> recipients)
    {
        var result = new List<string>(recipients.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in recipients)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var email = raw.Trim();
            if (!LooksLikeEmail(email))
            {
                throw new ArgumentException(
                    $"INV-TENANTSETTINGS-011 : destinataire de routage invalide « {email} » (e-mail attendu).",
                    nameof(recipients));
            }

            if (seen.Add(email))
            {
                result.Add(email);
            }
        }

        return result;
    }

    private static bool LooksLikeEmail(string value)
    {
        // Validation volontairement souple (le transport reste la garde finale) : un « @ » interne unique,
        // aucun espace, et un domaine ponctué. Suffit pour rejeter une saisie manifestement erronée.
        if (value.Contains(' '))
        {
            return false;
        }

        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@'))
        {
            return false;
        }

        var domain = value[(at + 1)..];
        return domain.Length >= 3 && domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.');
    }
}
