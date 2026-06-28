namespace Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Note de niveau document (EN 16931 BG-1) : un texte libre (BT-22 <see cref="Content"/>) éventuellement
/// qualifié par un code sujet (BT-21 <see cref="SubjectCode"/>, codelist UNTDID 4451). DTO PUR : aucune
/// règle fiscale, aucun texte inventé. Porte les mentions légales FR obligatoires entre professionnels
/// (converter CTC-FR / BR-FR-05) — pénalités de retard (PMD), indemnité forfaitaire de recouvrement (PMT),
/// escompte ou son absence (AAB) — dont le <see cref="Content"/> est un PARAMÈTRE TENANT « Mentions de
/// facturation » (F12-A §3.4), saisi par le client / son expert-comptable, jamais embarqué dans le code
/// (CLAUDE.md n°2/7). Voir F16 §3.5.
/// </summary>
public sealed class PivotDocumentNoteDto
{
    /// <summary>Crée une note de document.</summary>
    /// <param name="content">Texte de la note (EN 16931 BT-22). Obligatoire.</param>
    /// <param name="subjectCode">
    /// Code sujet UNTDID 4451 (EN 16931 BT-21) — ex. <c>PMD</c> / <c>PMT</c> / <c>AAB</c>. <c>null</c> pour
    /// une note libre sans code.
    /// </param>
    public PivotDocumentNoteDto(string content, string? subjectCode = null)
    {
        Content = content;
        SubjectCode = subjectCode;
    }

    /// <summary>Texte de la note (EN 16931 BT-22).</summary>
    public string Content { get; }

    /// <summary>Code sujet UNTDID 4451 (EN 16931 BT-21), <c>null</c> si absent.</summary>
    public string? SubjectCode { get; }
}
