namespace Liakont.Host.Documents;

using System;
using System.Collections.Generic;

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

    /// <summary><c>true</c> s'il y a des lignes à afficher (document transmis).</summary>
    public bool HasLines => Lines.Count > 0;

    /// <summary>Contenu vide (document non transmis) : aucune ligne, aucune charge, aucun contrôle.</summary>
    public static DocumentContentView Empty { get; } = new()
    {
        Lines = Array.Empty<DocumentLineView>(),
        Charges = Array.Empty<DocumentChargeView>(),
        Totals = null,
    };
}
