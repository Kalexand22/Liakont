namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Projection GÉNÉRIQUE des champs sortants d'un document qui ne se DÉRIVENT pas tels quels du pivot, à
/// destination du payload de la Plateforme Agréée (MND07, F15 §1.2). Lue par TOUT sérialiseur PA — elle
/// vit donc dans <c>Transmission.Contracts</c>, jamais dans un plug-in concret (CLAUDE.md n°6). Aujourd'hui
/// elle porte le cas de l'autofacturation sous mandat (type BT-3 = 389) : le <see cref="DocumentTypeCode"/>
/// devient « 389 » et le <see cref="FiscalNumber"/> est le BT-1 FISCAL ALLOUÉ PAR MANDANT (MND05/ADR-0025),
/// distinct du <c>Number</c> du pivot (qui reste l'identifiant source hashé — INV-BT1-1, ADR-0007 préservé).
/// <para>
/// Le vendeur fiscal (BT-30 SIREN mandant / BT-31 n° TVA mandant) n'est PAS porté ici : il EST déjà le
/// <c>Supplier</c> du pivot (BG-4) que les sérialiseurs mappent en <c>Seller</c> — l'autofacturation 389
/// est émise par le tenant mandataire au nom et pour le compte du mandant (art. 289 I-2 CGI, ADR-0022),
/// le mandant restant le vendeur. La projection ne convoie donc que ce qui DIFFÈRE du document standard.
/// </para>
/// <para>
/// La projection est <b>absente</b> (<c>null</c> passé à <c>IPaClient.SendDocumentAsync</c>) pour un
/// document standard : le plug-in conserve son comportement par défaut (type 380, BT-1 = <c>Number</c> du
/// pivot). Elle n'est construite que pour un document self-billed dont le BT-1 fiscal est déjà alloué.
/// </para>
/// </summary>
public sealed record PaOutboundProjection
{
    private PaOutboundProjection(string documentTypeCode, string fiscalNumber)
    {
        DocumentTypeCode = documentTypeCode;
        FiscalNumber = fiscalNumber;
    }

    /// <summary>Code de type de document EN 16931 BT-3 (UNTDID 1001) à projeter — voir <see cref="PaDocumentTypeCode"/>.</summary>
    public string DocumentTypeCode { get; }

    /// <summary>
    /// BT-1 à projeter dans le payload PA. En 389, c'est le BT-1 FISCAL alloué par mandant (MND05) — une
    /// valeur SÉPARÉE du <c>Number</c> hashé du pivot (INV-BT1-1) ; c'est aussi la clé d'unicité CTC
    /// (BT-1, BT-2, BT-30 mandant — Annexe 7 G1.42/G1.45, ADR-0025 §7).
    /// </summary>
    public string FiscalNumber { get; }

    /// <summary>
    /// Construit la projection d'une auto-facture sous mandat (type 389) avec son BT-1 fiscal alloué
    /// (MND05/ADR-0025). Le numéro fiscal est OBLIGATOIRE et non vide : « bloquer plutôt qu'émettre faux »
    /// (CLAUDE.md n°3) — un 389 sans BT-1 fiscal alloué ne doit jamais atteindre la PA (l'appelant garde
    /// l'absence en amont, jamais d'invention ici).
    /// </summary>
    /// <param name="allocatedFiscalNumber">BT-1 fiscal alloué par mandant (MND05). Jamais vide.</param>
    public static PaOutboundProjection ForSelfBilled(string allocatedFiscalNumber)
    {
        if (string.IsNullOrWhiteSpace(allocatedFiscalNumber))
        {
            throw new ArgumentException(
                "Le BT-1 fiscal alloué (MND05) est obligatoire pour projeter une auto-facture 389.",
                nameof(allocatedFiscalNumber));
        }

        return new PaOutboundProjection(PaDocumentTypeCode.SelfBilledInvoice, allocatedFiscalNumber);
    }
}
