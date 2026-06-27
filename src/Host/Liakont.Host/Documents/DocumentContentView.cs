namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Contenu affichable d'un document pour l'onglet « Contenu » du détail (FIX205, F10 §2.3) : les lignes, les
/// charges/remises de niveau document, et le contrôle de cohérence des totaux. PROJECTION en lecture du pivot
/// transmis (<see cref="DocumentLineProjection.FromTransmittedSnapshot"/>) — aucune règle métier, aucun calcul
/// fiscal inventé. <see cref="Totals"/> est <c>null</c> quand le document n'a pas encore été transmis (aucun
/// pivot mappé exposé) : la vue affiche alors une note, jamais une ligne ou un verdict inventés.
/// </summary>
public sealed record DocumentContentView
{
    /// <summary>Lignes du document tel que transmis (vide si non transmis).</summary>
    public required IReadOnlyList<DocumentLineView> Lines { get; init; }

    /// <summary>Charges/remises de niveau document (BG-20/BG-21), vide si aucune.</summary>
    public required IReadOnlyList<DocumentChargeView> Charges { get; init; }

    /// <summary>Contrôle de cohérence totaux ↔ lignes (S2.5), ou <c>null</c> si le document n'est pas transmis.</summary>
    public required DocumentTotalsCheck? Totals { get; init; }

    /// <summary>
    /// Termes / conditions de paiement EFFECTIFS du document (EN 16931 BT-20, BUG-26) — valeur du document, sinon
    /// défaut tenant (F12-A §3.4) ; <c>null</c> si aucun terme n'est paramétré. Donnée de l'entreprise, jamais inventée.
    /// </summary>
    public string? PaymentTerms { get; init; }

    /// <summary>
    /// Mentions légales FR EFFECTIVES du document (EN 16931 BG-1, BR-FR-05 : PMD/PMT/AAB, BUG-26) — valeur du
    /// document, sinon défaut tenant (F12-A §3.4) ; vide si aucune n'est paramétrée. Contenu tenant, jamais inventé.
    /// </summary>
    public IReadOnlyList<DocumentNoteView> Notes { get; init; } = Array.Empty<DocumentNoteView>();

    /// <summary><c>true</c> s'il y a des lignes à afficher (document transmis).</summary>
    public bool HasLines => Lines.Count > 0;

    /// <summary><c>true</c> si au moins une ligne est au régime de la marge (mention explicite portée) — pilote la note 297 E de REPLI (affichée seulement si le récap de marge chiffré est absent).</summary>
    public bool HasMarginLines => Lines.Any(line => line.MarginMention is not null);

    /// <summary><c>true</c> si au moins une mention de facturation effective est portée (termes de paiement OU note).</summary>
    public bool HasMentions => !string.IsNullOrWhiteSpace(PaymentTerms) || Notes.Count > 0;

    /// <summary>Contenu vide (document non transmis) : aucune ligne, aucune charge, aucun contrôle, aucune mention.</summary>
    public static DocumentContentView Empty { get; } = new()
    {
        Lines = Array.Empty<DocumentLineView>(),
        Charges = Array.Empty<DocumentChargeView>(),
        Totals = null,
    };
}
