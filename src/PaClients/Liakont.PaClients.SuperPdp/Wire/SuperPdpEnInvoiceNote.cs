namespace Liakont.PaClients.SuperPdp.Wire;

/// <summary>
/// Note de niveau document du schéma <c>en_invoice</c> de Super PDP (<c>invoice_note</c>, OpenAPI
/// v1.24.0.beta) — EN 16931 BG-1 : texte (BT-22 <c>note</c>) + code sujet (BT-21 <c>subject_code</c>,
/// codelist UNTDID 4451). Porte les mentions légales FR obligatoires (BR-FR-05 : PMD / PMT / AAB) dont
/// le contenu est un paramètre tenant (F12-A §3.4) — aucun texte inventé (CLAUDE.md n°2). Voir F16 §3.5.
/// </summary>
internal sealed record SuperPdpEnInvoiceNote
{
    /// <summary>Texte de la note (EN 16931 BT-22), sérialisé <c>note</c>.</summary>
    public required string Note { get; init; }

    /// <summary>Code sujet UNTDID 4451 (EN 16931 BT-21), sérialisé <c>subject_code</c> ; <c>null</c> ⇒ omis.</summary>
    public string? SubjectCode { get; init; }
}
