namespace Liakont.Modules.Pipeline.Contracts;

using System;
using System.Collections.Generic;
using System.Linq;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Résultat du rejeu read-time du contenu d'un document (BUG-5, <see cref="IDocumentContentReplayService"/>) :
/// le pivot relu/mappé qui alimente l'onglet « Contenu » du détail (lignes : libellé, montants, régime source →
/// catégorie/VATEX résultante du mapping) DÈS que le document est lu/contrôlé (états Bloqué / Prêt-à-envoyer),
/// sans attendre la transmission.
/// <para>
/// <see cref="MappedPivot"/> porte le pivot ENRICHI (catégorie/VATEX/taux posés par le mapping validé) quand le
/// rejeu PASSE ; à défaut (le mapping BLOQUE — ex. régime non couvert), il porte le pivot SOURCE tel que lu —
/// régime source présent, catégorie/VATEX VIDES (le diagnostic FACTUEL du blocage, JAMAIS une valeur inventée,
/// CLAUDE.md n°2). <see cref="Available"/> est <c>false</c> quand le pivot source stagé n'est plus disponible
/// (purgé après émission, ou intégrité KO) : l'appelant retombe alors sur le snapshot transmis (comportement
/// historique préservé). AUCUN montant n'est recalculé (le pivot fait foi, F01-F02).
/// </para>
/// <para>
/// <see cref="PaymentTerms"/> (BT-20) et <see cref="Notes"/> (BG-1 : mentions légales FR BR-FR-05) sont les
/// mentions de facturation EFFECTIVES du document (BUG-26, F12-A §3.4) — valeur du document si portée, sinon
/// défaut TENANT injecté par l'enricher au read-time (jamais inventées, CLAUDE.md n°2). Vides quand aucune mention
/// n'est paramétrée, ou quand la société du tenant n'est pas connue (paramétrage incomplet).
/// </para>
/// </summary>
public sealed record DocumentContentReplay
{
    /// <summary>
    /// Le pivot relu/mappé à afficher (enrichi si le mapping passe, source si le mapping bloque), ou <c>null</c>
    /// quand le contenu stagé n'est plus disponible (<see cref="Available"/> = <c>false</c>).
    /// </summary>
    public PivotDocumentDto? MappedPivot { get; init; }

    /// <summary>
    /// Termes / conditions de paiement EFFECTIFS du document (EN 16931 BT-20) — valeur du document, sinon défaut
    /// tenant (F12-A §3.4) ; <c>null</c> si aucun terme n'est paramétré. Donnée de l'entreprise, jamais inventée.
    /// </summary>
    public string? PaymentTerms { get; init; }

    /// <summary>
    /// Mentions légales FR EFFECTIVES du document (EN 16931 BG-1, BR-FR-05 : PMD/PMT/AAB) — valeur du document,
    /// sinon défaut tenant (F12-A §3.4) ; vide si aucune mention n'est paramétrée. Contenu tenant, jamais inventé.
    /// </summary>
    public IReadOnlyList<DocumentContentNote> Notes { get; init; } = Array.Empty<DocumentContentNote>();

    /// <summary><c>true</c> si le pivot source stagé a pu être relu (le contenu est restituable), <c>false</c> sinon.</summary>
    public bool Available => MappedPivot is not null;

    /// <summary>Contenu stagé indisponible (purgé après émission, absent, ou intégrité KO) — l'appelant retombe sur le snapshot transmis.</summary>
    public static DocumentContentReplay Unavailable { get; } = new();

    /// <summary>
    /// Le pivot relu (enrichi si mapping prêt, source si mapping bloqué) à projeter en lignes. Les mentions de
    /// facturation EFFECTIVES (BT-20 + notes BG-1) sont lues SUR le pivot porté (l'appelant l'enrichit au préalable
    /// avec le défaut tenant — BUG-26) : un pivot sans mention donne des mentions vides.
    /// </summary>
    public static DocumentContentReplay From(PivotDocumentDto mappedPivot) => new()
    {
        MappedPivot = mappedPivot,
        PaymentTerms = mappedPivot.PaymentTerms,
        Notes = mappedPivot.Notes is { Count: > 0 } notes
            ? notes.Select(note => new DocumentContentNote(note.Content, note.SubjectCode)).ToList()
            : Array.Empty<DocumentContentNote>(),
    };
}
