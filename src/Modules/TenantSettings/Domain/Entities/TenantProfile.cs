namespace Liakont.Modules.TenantSettings.Domain.Entities;

using Liakont.Modules.TenantSettings.Domain.Services;
using Liakont.Modules.TenantSettings.Domain.ValueObjects;

/// <summary>
/// Profil légal et administratif d'un tenant (= 1 client final, F12-A §2).
/// Le SIREN est la clé fonctionnelle du tenant (validée Luhn). Le profil vit dans la base
/// PAR TENANT et est scopé <c>company_id</c> (isolation socle Stratum + CLAUDE.md n°9).
/// </summary>
public sealed class TenantProfile
{
    private TenantProfile()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Siren { get; private set; } = string.Empty;

    public string RaisonSociale { get; private set; } = string.Empty;

    public TenantAddress Address { get; private set; } = null!;

    public string? ContactEmailAlerte { get; private set; }

    public TenantStatus Statut { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public static TenantProfile Create(
        Guid companyId,
        string siren,
        string raisonSociale,
        TenantAddress address,
        string? contactEmailAlerte)
    {
        ValidateSiren(siren);
        ValidateRaisonSociale(raisonSociale);
        ArgumentNullException.ThrowIfNull(address);
        ValidateEmail(contactEmailAlerte);

        return new TenantProfile
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Siren = siren,
            RaisonSociale = raisonSociale.Trim(),
            Address = address,
            ContactEmailAlerte = NormalizeEmail(contactEmailAlerte),
            Statut = TenantStatus.Actif,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    public static TenantProfile Reconstitute(
        Guid id,
        Guid companyId,
        string siren,
        string raisonSociale,
        TenantAddress address,
        string? contactEmailAlerte,
        TenantStatus statut,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new TenantProfile
        {
            Id = id,
            CompanyId = companyId,
            Siren = siren,
            RaisonSociale = raisonSociale,
            Address = address,
            ContactEmailAlerte = contactEmailAlerte,
            Statut = statut,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    /// <summary>Met à jour les informations modifiables du profil (le SIREN reste la clé fonctionnelle, immuable).</summary>
    public void UpdateDetails(string raisonSociale, TenantAddress address, string? contactEmailAlerte)
    {
        ValidateRaisonSociale(raisonSociale);
        ArgumentNullException.ThrowIfNull(address);
        ValidateEmail(contactEmailAlerte);

        RaisonSociale = raisonSociale.Trim();
        Address = address;
        ContactEmailAlerte = NormalizeEmail(contactEmailAlerte);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Met à jour le SEUL e-mail de contact d'alerte du tenant (F12-A §2 / F12 §5.3), sans toucher au reste
    /// du profil. Destinataire des alertes critiques quand l'option <c>AlertTenantContact</c> est active
    /// (seuils, CFG02). Vide ⇒ aucun contact (normalisé en <c>null</c>).
    /// </summary>
    public void SetAlertContactEmail(string? contactEmailAlerte)
    {
        ValidateEmail(contactEmailAlerte);
        ContactEmailAlerte = NormalizeEmail(contactEmailAlerte);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Suspend()
    {
        if (Statut == TenantStatus.Suspendu)
        {
            return;
        }

        Statut = TenantStatus.Suspendu;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Reactivate()
    {
        if (Statut == TenantStatus.Actif)
        {
            return;
        }

        Statut = TenantStatus.Actif;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ValidateSiren(string siren)
    {
        if (!SirenValidator.IsValid(siren))
        {
            throw new ArgumentException(
                "INV-TENANTSETTINGS-001 : le SIREN doit comporter 9 chiffres et satisfaire la clé de Luhn.",
                nameof(siren));
        }
    }

    private static void ValidateRaisonSociale(string raisonSociale)
    {
        if (string.IsNullOrWhiteSpace(raisonSociale))
        {
            throw new ArgumentException("INV-TENANTSETTINGS-002 : la raison sociale est obligatoire.", nameof(raisonSociale));
        }
    }

    private static void ValidateEmail(string? email)
    {
        if (email is null)
        {
            return;
        }

        var trimmed = email.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        var at = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at == trimmed.Length - 1 || trimmed.Contains(' ', StringComparison.Ordinal))
        {
            throw new ArgumentException("INV-TENANTSETTINGS-002 : l'email de contact d'alerte est invalide.", nameof(email));
        }
    }

    private static string? NormalizeEmail(string? email)
    {
        if (email is null)
        {
            return null;
        }

        var trimmed = email.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
