namespace Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Un tiers du document (émetteur, destinataire, émetteur de facture, bénéficiaire) — EN 16931
/// BG-4 / BG-7 / BG-10. DTO pur : aucune logique, aucune heuristique.
/// </summary>
public sealed class PivotPartyDto
{
    /// <summary>Crée un tiers pivot.</summary>
    /// <param name="name">Raison sociale / nom (EN 16931 BT-27 / BT-44). Obligatoire.</param>
    /// <param name="siren">SIREN (EN 16931 BT-30, scheme 0002). Absent = <c>null</c>.</param>
    /// <param name="siret">SIRET (EN 16931 BT-30, scheme 0009). Absent = <c>null</c>.</param>
    /// <param name="vatNumber">N° TVA intracommunautaire (EN 16931 BT-31). Absent = <c>null</c>.</param>
    /// <param name="address">Adresse postale (BG-5 / BG-8). Absent = <c>null</c>.</param>
    /// <param name="email">Courriel (notifications uniquement, non transmis à la DGFiP).</param>
    /// <param name="isCompanyHint">
    /// Transcription BRUTE d'un indice de qualité « société » porté par la source (ex. champ
    /// <c>societe</c> non vide) — AUCUNE heuristique côté agent (F01-F02 §3.2, amendement
    /// 2026-06-03). Toute décision (forme juridique, n° TVA, bascule B2B, blocage) vit dans le
    /// module Validation de la plateforme (VAL05).
    /// </param>
    public PivotPartyDto(
        string name,
        string? siren = null,
        string? siret = null,
        string? vatNumber = null,
        PivotAddressDto? address = null,
        string? email = null,
        bool isCompanyHint = false)
    {
        Name = name;
        Siren = siren;
        Siret = siret;
        VatNumber = vatNumber;
        Address = address;
        Email = email;
        IsCompanyHint = isCompanyHint;
    }

    /// <summary>Raison sociale / nom (EN 16931 BT-27 / BT-44).</summary>
    public string Name { get; }

    /// <summary>SIREN (EN 16931 BT-30, scheme 0002).</summary>
    public string? Siren { get; }

    /// <summary>SIRET (EN 16931 BT-30, scheme 0009).</summary>
    public string? Siret { get; }

    /// <summary>N° TVA intracommunautaire (EN 16931 BT-31).</summary>
    public string? VatNumber { get; }

    /// <summary>Adresse postale (EN 16931 BG-5 / BG-8).</summary>
    public PivotAddressDto? Address { get; }

    /// <summary>Courriel (notifications uniquement, non transmis à la DGFiP).</summary>
    public string? Email { get; }

    /// <summary>
    /// Indice BRUT « société » porté par la source (aucune heuristique côté agent —
    /// l'interprétation vit dans Validation, VAL05).
    /// </summary>
    public bool IsCompanyHint { get; }
}
