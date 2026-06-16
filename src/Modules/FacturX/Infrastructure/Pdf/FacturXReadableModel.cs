namespace Liakont.Modules.FacturX.Infrastructure.Pdf;

using System;
using System.Collections.Generic;

/// <summary>
/// Modèle de présentation du rendu visuel lisible du Factur-X (F16 §5). Reprend la FORME du
/// <c>ArchiveReadableDocument</c> du module Archive, mais est défini LOCALEMENT dans FacturX :
/// l'invariant ADR-0023 INV-FX-4 interdit à FacturX de référencer un autre module (même
/// <c>*.Contracts</c>) — la sortie doit être déterministe du pivot seul. DTO PUR (aucune logique,
/// aucune règle fiscale, CLAUDE.md n°2) ; tous les montants en <see cref="decimal"/> (n°1). Les
/// libellés (type de document, taux/catégorie/VATEX) sont RECOPIÉS du pivot, jamais inventés.
/// </summary>
/// <param name="DocumentNumber">Numéro du document (EN 16931 BT-1).</param>
/// <param name="DocumentTypeLabel">Libellé du type (« Facture » / « Avoir »), déduit du pivot.</param>
/// <param name="IssueDate">Date d'émission (EN 16931 BT-2).</param>
/// <param name="DueDate">Date d'échéance (EN 16931 BT-9), ou <c>null</c> si le pivot n'en porte pas.</param>
/// <param name="CurrencyCode">Devise ISO 4217 (EN 16931 BT-5).</param>
/// <param name="SellerName">Raison sociale du vendeur (EN 16931 BG-4).</param>
/// <param name="SellerSiren">SIREN du vendeur (EN 16931 BT-30), ou <c>null</c>.</param>
/// <param name="SellerVatNumber">N° TVA intracommunautaire du vendeur (EN 16931 BT-31), ou <c>null</c>.</param>
/// <param name="BuyerName">Nom de l'acheteur (EN 16931 BT-44), ou <c>null</c> (B2C non identifié).</param>
/// <param name="Lines">Lignes du document.</param>
/// <param name="VatBreakdown">Ventilation de TVA par taux/catégorie/VATEX (libellés recopiés).</param>
/// <param name="TotalNet">Total HT (EN 16931 BT-109).</param>
/// <param name="TotalTax">Total de TVA (EN 16931 BT-110).</param>
/// <param name="TotalGross">Total TTC (EN 16931 BT-112).</param>
/// <param name="Prepaid">Acompte déjà payé (EN 16931 BT-113), ou <c>null</c>.</param>
/// <param name="DuePayable">Net à payer (EN 16931 BT-115 = BT-112 − BT-113).</param>
internal sealed record FacturXReadableModel(
    string DocumentNumber,
    string DocumentTypeLabel,
    DateOnly IssueDate,
    DateOnly? DueDate,
    string CurrencyCode,
    string SellerName,
    string? SellerSiren,
    string? SellerVatNumber,
    string? BuyerName,
    IReadOnlyList<FacturXReadableLine> Lines,
    IReadOnlyList<FacturXReadableVatLine> VatBreakdown,
    decimal TotalNet,
    decimal TotalTax,
    decimal TotalGross,
    decimal? Prepaid,
    decimal DuePayable);
