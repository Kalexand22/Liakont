namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Modules.TenantSettings.Contracts.Queries;
using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Identity;

/// <summary>
/// Contrôle d'identité de l'émetteur (F04 §3.1). Le SIREN émetteur de référence vient du PROFIL DU
/// TENANT (CFG02), jamais du document (note v6, item VAL02) : la règle vérifie que ce SIREN est
/// paramétré, valide (Luhn), et — si le document porte lui-même un SIREN émetteur — qu'il est
/// cohérent avec le profil. Toutes les anomalies sont BLOQUANTES (jamais affaiblies — CLAUDE.md n°3).
/// </summary>
public sealed class SupplierIdentityRule : IDocumentRule
{
    /// <summary>Le SIREN de l'émetteur n'est pas paramétré pour ce tenant (profil incomplet).</summary>
    public const string SirenMissing = "SUPPLIER_SIREN_MISSING";

    /// <summary>Le SIREN émetteur paramétré est invalide (clé de Luhn).</summary>
    public const string SirenInvalid = "SUPPLIER_SIREN_INVALID";

    /// <summary>Le SIREN émetteur porté par le document ne correspond pas au profil du tenant.</summary>
    public const string SirenMismatch = "SUPPLIER_SIREN_MISMATCH";

    private readonly ITenantSettingsQueries _tenantSettingsQueries;

    /// <summary>Crée la règle avec l'accès en lecture au paramétrage tenant (TenantSettings.Contracts).</summary>
    /// <param name="tenantSettingsQueries">Lectures du profil tenant, scopées par <c>CompanyId</c>. Obligatoire.</param>
    public SupplierIdentityRule(ITenantSettingsQueries tenantSettingsQueries)
    {
        _tenantSettingsQueries = tenantSettingsQueries ?? throw new ArgumentNullException(nameof(tenantSettingsQueries));
    }

    /// <inheritdoc/>
    public string Code => "SUPPLIER_IDENTITY";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var document = context.Document;
        var issues = new List<ValidationIssue>();

        var profile = await _tenantSettingsQueries.GetTenantProfile(context.CompanyId, cancellationToken);
        var issuerSiren = profile?.Siren;

        if (string.IsNullOrWhiteSpace(issuerSiren))
        {
            issues.Add(ValidationIssue.Blocking(
                SirenMissing,
                $"Le SIREN de l'émetteur n'est pas paramétré pour ce tenant. Complétez le profil de l'entreprise dans Liakont avant d'envoyer le document n° {document.Number}.",
                $"TenantProfile.Siren absent pour companyId={context.CompanyId}.",
                "BT-30"));

            // Sans SIREN émetteur de référence, les contrôles aval (validité, cohérence) n'ont pas de base.
            return issues;
        }

        if (!SirenValidator.IsValid(issuerSiren))
        {
            issues.Add(ValidationIssue.Blocking(
                SirenInvalid,
                $"Le SIREN de l'émetteur paramétré ({issuerSiren}) est invalide (clé de Luhn). Corrigez le profil de l'entreprise dans Liakont avant d'envoyer le document n° {document.Number}.",
                $"Le SIREN émetteur '{issuerSiren}' (profil tenant) échoue la validation Luhn (F04 §4.1).",
                "BT-30"));

            // Émetteur mal paramétré : la cohérence document/profil n'a pas de sens tant qu'il n'est pas corrigé.
            return issues;
        }

        var documentSiren = document.Supplier.Siren;
        if (!string.IsNullOrWhiteSpace(documentSiren) && !string.Equals(documentSiren, issuerSiren, StringComparison.Ordinal))
        {
            issues.Add(ValidationIssue.Blocking(
                SirenMismatch,
                $"Le SIREN émetteur du document n° {document.Number} ({documentSiren}) ne correspond pas au SIREN de l'entreprise paramétrée dans Liakont ({issuerSiren}). Vérifiez que ce document provient bien de votre établissement.",
                $"Document Supplier.Siren='{documentSiren}' ≠ TenantProfile.Siren='{issuerSiren}'.",
                "BT-30"));
        }

        return issues;
    }
}
