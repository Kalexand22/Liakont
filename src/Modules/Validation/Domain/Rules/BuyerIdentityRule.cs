namespace Liakont.Modules.Validation.Domain.Rules;

using Liakont.Modules.Validation.Contracts;
using Liakont.Modules.Validation.Domain.Identity;

/// <summary>
/// Contrôle d'identité de l'acheteur (F04 §3.2). Le SIREN acheteur n'est contrôlé que s'il est
/// présent (B2B optionnel en V1) ; le code pays acheteur, lorsqu'il est renseigné, doit appartenir
/// à ISO 3166-1 alpha-2 (il détermine B2C vs international). Les anomalies sont BLOQUANTES
/// (CLAUDE.md n°3). La règle est pure : elle ne lit que le document (aucune dépendance externe).
/// </summary>
public sealed class BuyerIdentityRule : IDocumentRule
{
    /// <summary>Le SIREN de l'acheteur, présent dans le document, est invalide (clé de Luhn).</summary>
    public const string BuyerSirenInvalid = "BUYER_SIREN_INVALID";

    /// <summary>Le code pays de l'acheteur, présent dans le document, n'est pas un code ISO 3166-1 alpha-2.</summary>
    public const string BuyerCountryInvalid = "BUYER_COUNTRY_INVALID";

    /// <inheritdoc/>
    public string Code => "BUYER_IDENTITY";

    /// <inheritdoc/>
    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(DocumentValidationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var document = context.Document;
        var buyer = document.Customer;
        var issues = new List<ValidationIssue>();

        if (buyer is null)
        {
            // B2C sans tiers identifié : aucun contrôle d'identité acheteur (pivot autorise Customer = null).
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
        }

        var buyerSiren = buyer.Siren;
        if (!string.IsNullOrWhiteSpace(buyerSiren) && !SirenValidator.IsValid(buyerSiren))
        {
            issues.Add(ValidationIssue.Blocking(
                BuyerSirenInvalid,
                $"Le SIREN de l'acheteur ({buyerSiren}) du document n° {document.Number} est invalide (clé de Luhn). Vérifiez l'identité de l'acheteur dans le logiciel source.",
                $"Buyer SIREN '{buyerSiren}' échoue la validation Luhn (F04 §3.2/§4.1).",
                "BT-47"));
        }

        var buyerCountry = buyer.Address?.CountryCode;
        if (!string.IsNullOrWhiteSpace(buyerCountry) && !CountryCodeValidator.IsValid(buyerCountry))
        {
            issues.Add(ValidationIssue.Blocking(
                BuyerCountryInvalid,
                $"Le code pays de l'acheteur (« {buyerCountry} ») du document n° {document.Number} n'est pas un code pays ISO 3166-1 alpha-2 valide. Corrigez le pays de l'acheteur dans le logiciel source.",
                $"Buyer country code '{buyerCountry}' n'appartient pas à ISO 3166-1 alpha-2 (F04 §3.2).",
                "BT-55"));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
