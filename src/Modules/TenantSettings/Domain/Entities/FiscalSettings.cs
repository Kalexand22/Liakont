namespace Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Paramétrage fiscal d'un tenant (F12-A §3). Les décisions appartiennent à l'expert-comptable
/// du client : le produit ne tranche jamais à sa place.
/// </summary>
/// <remarks>
/// <para><strong>INV-TENANTSETTINGS-004 :</strong> tout paramètre <c>null</c> = décision en attente
/// = transmissions concernées suspendues (jamais de valeur par défaut, jamais de règle devinée —
/// CLAUDE.md n°2/3). Cette entité ne fait que STOCKER ; la suspension est appliquée par les
/// consommateurs (PIP03, SUP01, WEB01).</para>
/// <para><strong>INV-TENANTSETTINGS-008 :</strong> <see cref="ReportingFrequency"/> est volontairement
/// stocké en chaîne OPAQUE : l'énumération exacte (régime vs fréquence ; « trimestrielle » de D4 vs
/// « bimestrielle » de F09 §2) n'est PAS tranchée (F12-A §3.3). Figer une énumération ici reviendrait
/// à inventer une règle fiscale (CLAUDE.md n°2). CFG02 ne l'interprète jamais.</para>
/// </remarks>
public sealed class FiscalSettings
{
    private FiscalSettings()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Option TVA sur les débits (F09 §2/§6). <c>null</c> = e-reporting de paiement suspendu.</summary>
    public bool? VatOnDebits { get; private set; }

    /// <summary>Nature de l'opération (F01-F02/F09 §1). <c>null</c> = transmissions dépendantes suspendues.</summary>
    public OperationCategory? OperationCategory { get; private set; }

    /// <summary>
    /// Cadence déclarative (F12-A §3.3) — chaîne OPAQUE, énumération NON figée (point à trancher
    /// avec l'expert-comptable). <c>null</c> = pas de calcul d'échéance + e-reporting paiement suspendu.
    /// </summary>
    public string? ReportingFrequency { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Crée un paramétrage fiscal. Tous les champs sont optionnels (défaut <c>null</c> = suspension).</summary>
    public static FiscalSettings Create(
        Guid companyId,
        bool? vatOnDebits,
        OperationCategory? operationCategory,
        string? reportingFrequency)
    {
        return new FiscalSettings
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            VatOnDebits = vatOnDebits,
            OperationCategory = operationCategory,
            ReportingFrequency = NormalizeOpaque(reportingFrequency),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    public static FiscalSettings Reconstitute(
        Guid id,
        Guid companyId,
        bool? vatOnDebits,
        OperationCategory? operationCategory,
        string? reportingFrequency,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new FiscalSettings
        {
            Id = id,
            CompanyId = companyId,
            VatOnDebits = vatOnDebits,
            OperationCategory = operationCategory,
            ReportingFrequency = reportingFrequency,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    /// <summary>Met à jour le paramétrage fiscal. <c>null</c> est une valeur valide et signifiante (suspension).</summary>
    public void Update(bool? vatOnDebits, OperationCategory? operationCategory, string? reportingFrequency)
    {
        VatOnDebits = vatOnDebits;
        OperationCategory = operationCategory;
        ReportingFrequency = NormalizeOpaque(reportingFrequency);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string? NormalizeOpaque(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
