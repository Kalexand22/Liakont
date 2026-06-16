namespace Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Mandant d'autofacturation (F15 §2.2, ADR-0022) : le vendeur au nom et pour le compte duquel le
/// tenant (mandataire) émet les factures de type 389 (art. 289 I-2 CGI). C'est un <b>tiers récurrent</b>
/// du tenant — une donnée de référence réutilisée document après document — <b>jamais un sous-tenant</b>
/// (blueprint §7, INV-MANDATS-1). Clé métier : (<c>company_id</c>, <c>reference</c>). Le mandant ne porte
/// <b>aucun montant</b> (ADR-0022) ; les valeurs réelles (raison sociale, n° TVA, SIREN, préfixe) sont du
/// paramétrage tenant, jamais embarquées dans le code (CLAUDE.md n°7, INV-MANDATS-5).
/// </summary>
public sealed class Mandant
{
    private Mandant()
    {
        Reference = string.Empty;
        RaisonSociale = string.Empty;
        Siren = string.Empty;
        NumberingPrefix = string.Empty;
    }

    /// <summary>Identifiant technique du mandant.</summary>
    public Guid Id { get; private set; }

    /// <summary>Tenant propriétaire (isolation par société — CLAUDE.md n°9, INV-MANDATS-1).</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>Référence métier du mandant (clé tenant-scopée avec <see cref="CompanyId"/>) — paramétrage tenant.</summary>
    public string Reference { get; private set; }

    /// <summary>Raison sociale du mandant (paramétrage tenant).</summary>
    public string RaisonSociale { get; private set; }

    /// <summary>
    /// Numéro de TVA du mandant (BT-31, F15 §2.2). <c>null</c> = non renseigné — le mandat reste suspendu
    /// tant que les conditions d'émission ne sont pas réunies (la garde 389 vit au niveau du <see cref="Mandat"/>).
    /// </summary>
    public string? SellerVatNumber { get; private set; }

    /// <summary>SIREN du mandant (BT-30 en flux 389 — le « fournisseur » fiscal est le mandant, F15 §1.8).</summary>
    public string Siren { get; private set; }

    /// <summary>
    /// Racine/préfixe de numérotation propre au mandant (BOI-TVA-DECLA-30-20-20-10 §130, F15 §1.4) —
    /// paramétrage tenant. La séquence elle-même (<c>MandatSequence</c>) est livrée par MND05 ; MND01 ne
    /// porte que le préfixe déclaré.
    /// </summary>
    public string NumberingPrefix { get; private set; }

    /// <summary>Date de création (UTC).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Date de dernière modification (UTC), <c>null</c> si jamais modifiée.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>
    /// Crée un nouveau mandant (chemin d'écriture). Rejette toute valeur structurellement absente
    /// (référence, raison sociale, SIREN, préfixe) — aucune valeur par défaut inventée (CLAUDE.md n°2/3).
    /// <paramref name="sellerVatNumber"/> est facultatif (BT-31 nullable).
    /// </summary>
    public static Mandant Create(
        Guid companyId,
        string reference,
        string raisonSociale,
        string? sellerVatNumber,
        string siren,
        string numberingPrefix)
    {
        return new Mandant
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Reference = RequireText(reference, "la référence du mandant"),
            RaisonSociale = RequireText(raisonSociale, "la raison sociale du mandant"),
            SellerVatNumber = NormalizeOptional(sellerVatNumber),
            Siren = RequireText(siren, "le SIREN du mandant"),
            NumberingPrefix = RequireText(numberingPrefix, "le préfixe de numérotation du mandant"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = null,
        };
    }

    /// <summary>Reconstitue un mandant depuis la base (chemin de chargement) — sans re-générer l'identité.</summary>
    public static Mandant Reconstitute(
        Guid id,
        Guid companyId,
        string reference,
        string raisonSociale,
        string? sellerVatNumber,
        string siren,
        string numberingPrefix,
        DateTimeOffset createdAt,
        DateTimeOffset? updatedAt)
    {
        return new Mandant
        {
            Id = id,
            CompanyId = companyId,
            Reference = reference,
            RaisonSociale = raisonSociale,
            SellerVatNumber = sellerVatNumber,
            Siren = siren,
            NumberingPrefix = numberingPrefix,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    /// <summary>
    /// Met à jour les coordonnées de référence du mandant (raison sociale, n° TVA, SIREN, préfixe).
    /// Le mandant est de la donnée de référence : la mutation est en place (pas de versionnage), mais elle
    /// est journalisée append-only (INV-MANDATS-3). La clé (<see cref="CompanyId"/>, <see cref="Reference"/>)
    /// n'est jamais changée par cette voie.
    /// </summary>
    public void UpdateDetails(string raisonSociale, string? sellerVatNumber, string siren, string numberingPrefix)
    {
        RaisonSociale = RequireText(raisonSociale, "la raison sociale du mandant");
        SellerVatNumber = NormalizeOptional(sellerVatNumber);
        Siren = RequireText(siren, "le SIREN du mandant");
        NumberingPrefix = RequireText(numberingPrefix, "le préfixe de numérotation du mandant");
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static string RequireText(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"Champ obligatoire absent : {label} (aucune valeur par défaut inventée, CLAUDE.md n°2).",
                label);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
