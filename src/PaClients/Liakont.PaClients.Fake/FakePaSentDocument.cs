namespace Liakont.PaClients.Fake;

/// <summary>
/// Instantané d'un document RÉELLEMENT transmis au plug-in factice (MND07) — exploitable en assertion de
/// test pour prouver la PROJECTION sortante (type BT-3 projeté, BT-1 émis, vendeur fiscal). Le plug-in
/// factice est un double de test : il ne fabrique aucune donnée, il RECOPIE ce que le pipeline lui passe.
/// </summary>
/// <param name="DocumentTypeCode">Code de type EN 16931 BT-3 projeté (« 389 » self-billed, « 380 » standard).</param>
/// <param name="FiscalNumber">BT-1 émis vers la PA (en 389 : le numéro fiscal alloué par mandant — MND05).</param>
/// <param name="SourceNumber">Numéro source du pivot (clé d'idempotence interne — <c>Number</c>).</param>
/// <param name="IsSelfBilled">Indique si le pivot transmis est une auto-facture sous mandat.</param>
/// <param name="SellerSiren">SIREN du vendeur fiscal projeté (BT-30 ; en 389 = le mandant), ou <c>null</c>.</param>
/// <param name="SellerVatNumber">N° TVA du vendeur fiscal projeté (BT-31 ; en 389 = le mandant), ou <c>null</c>.</param>
/// <param name="BuyerCountryCode">Code pays acheteur (BT-55) TEL QUE TRANSMIS — normalisé ISO au read-time
/// (ADR-0038) : prouve que le payload sortant porte le code ISO (« GB »), jamais le code legacy source (« ENG »).</param>
public sealed record FakePaSentDocument(
    string DocumentTypeCode,
    string FiscalNumber,
    string SourceNumber,
    bool IsSelfBilled,
    string? SellerSiren,
    string? SellerVatNumber,
    string? BuyerCountryCode);
