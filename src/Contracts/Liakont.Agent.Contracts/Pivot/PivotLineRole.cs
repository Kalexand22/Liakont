namespace Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Rôle STRUCTUREL d'une ligne de document — transcription BRUTE d'une distinction portée par la SOURCE
/// (F01-F02 §3.7), JAMAIS une décision fiscale (CLAUDE.md n°6 : la catégorie/VATEX/taux restent décidés par
/// le mapping plateforme). Permet à la PLATEFORME de distinguer, sur un bordereau d'enchères, l'adjudication
/// du lot de l'HONORAIRE acheteur (commission) — tous deux portés en lignes depuis l'amendement F03 §2.3
/// (2026-06-26, BUG-17 volet b). Le calcul de la marge B2C (jobs B4, F03 §2.4/§2.5) et l'aiguillage ont besoin
/// de savoir QUELLE ligne est la commission (les deux peuvent partager la même catégorie E+VATEX sous le régime
/// de la marge). Valeur ADDITIVE (ADR-0007) : <see cref="Standard"/> par défaut — toute ligne d'un document
/// ordinaire (facture, avoir) ou une adjudication de lot est <see cref="Standard"/> et n'émet PAS le champ au
/// JSON canonique (hash-neutre, seul un rôle NON-défaut est porté — pattern EXT01).
/// </summary>
public enum PivotLineRole
{
    /// <summary>
    /// Ligne ordinaire (DÉFAUT) : ligne de facture, d'avoir, ou adjudication d'un lot d'enchères. Émise SANS
    /// le champ « Role » au JSON canonique (hash-neutre — seul un rôle non-défaut est porté).
    /// </summary>
    Standard = 0,

    /// <summary>
    /// HONORAIRE ACHETEUR (commission / frais acheteur) d'un bordereau d'enchères — DONNÉE DE CALCUL de la
    /// marge B2C (F03 §2.4/§2.5), portée en LIGNE (et non plus dans un side-channel hors-lignes) depuis
    /// l'amendement F03 §2.3 (2026-06-26). Son <see cref="PivotLineDto.NetAmount"/> est porté TTC ; sous le
    /// régime de la marge la ligne ne porte AUCUNE TVA distincte (art. 297 E) et sa TVA-marge est calculée par
    /// le job B4 (mapping part Frais). L'honoraire VENDEUR, lui, reste hors-lignes (décompte vendeur BV,
    /// <see cref="PivotDocumentDto.SellerFees"/>) — il n'est pas sur le bordereau de l'acheteur.
    /// </summary>
    BuyerFee = 1,
}
