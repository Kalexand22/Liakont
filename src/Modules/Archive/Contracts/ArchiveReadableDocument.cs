namespace Liakont.Modules.Archive.Contracts;

using System;
using System.Collections.Generic;

/// <summary>
/// Données nécessaires au rendu lisible autonome d'un document (<c>document-lisible.html</c>, exigence de
/// lisibilité art. 289 V CGI — TRK05 §2). DTO PUR de présentation : l'appelant (pipeline) le projette
/// depuis le pivot transmis ; le module Archive ne classe rien et n'invente aucune règle fiscale — il
/// reçoit des libellés déjà calculés (type de document, taux/exonération). Tous les montants sont en
/// <see cref="decimal"/> (CLAUDE.md n°1).
/// </summary>
/// <param name="DocumentNumber">Numéro du document (EN 16931 BT-1).</param>
/// <param name="DocumentTypeLabel">Libellé du type (« Facture », « Avoir »…), fourni par l'appelant.</param>
/// <param name="IssueDate">Date d'émission (EN 16931 BT-2).</param>
/// <param name="CurrencyCode">Devise ISO 4217.</param>
/// <param name="SellerName">Raison sociale du vendeur (EN 16931 BG-4).</param>
/// <param name="SellerSiren">SIREN du vendeur, ou <c>null</c> si absent.</param>
/// <param name="BuyerName">Nom de l'acheteur, ou <c>null</c> (B2C sans tiers identifié).</param>
/// <param name="Lines">Lignes du document.</param>
/// <param name="VatBreakdown">Ventilation de TVA par taux/exonération (libellés fournis par l'appelant).</param>
/// <param name="TotalNet">Total HT (EN 16931 BT-109).</param>
/// <param name="TotalTax">Total de TVA (EN 16931 BT-110).</param>
/// <param name="TotalGross">Total TTC (EN 16931 BT-112).</param>
public sealed record ArchiveReadableDocument(
    string DocumentNumber,
    string DocumentTypeLabel,
    DateOnly IssueDate,
    string CurrencyCode,
    string SellerName,
    string? SellerSiren,
    string? BuyerName,
    IReadOnlyList<ArchiveReadableLine> Lines,
    IReadOnlyList<ArchiveVatBreakdownLine> VatBreakdown,
    decimal TotalNet,
    decimal TotalTax,
    decimal TotalGross);
