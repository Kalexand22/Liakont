namespace Liakont.Modules.Ged.Domain.Graph;

using System;
using System.Collections.Generic;

/// <summary>
/// Modes de dérivation d'une relation entité↔entité GED (F19 §10, GED24). Vocabulaire TECHNIQUE fermé et
/// GÉNÉRIQUE (aucun métier) — miroir Domain de la contrainte <c>ck_rir_mode</c> de
/// <c>ged_catalog.relation_inference_rules</c> :
/// <list type="bullet">
/// <item><description><see cref="Transitive"/> : fermeture transitive d'un genre (<c>inferred</c>).</description></item>
/// <item><description><see cref="Hierarchical"/> : héritage le long d'un genre parent-enfant (<c>inherited</c>).</description></item>
/// </list>
/// </summary>
public static class RelationInferenceMode
{
    /// <summary>Fermeture transitive d'un genre : A─k─▶B ─k─▶C ⇒ A─k─▶C (<c>relation_type='inferred'</c>).</summary>
    public const string Transitive = "transitive";

    /// <summary>Héritage le long d'un genre parent-enfant (<c>relation_type='inherited'</c>).</summary>
    public const string Hierarchical = "hierarchical";

    /// <summary>Vocabulaire fermé des modes (miroir <c>ck_rir_mode</c>).</summary>
    public static readonly IReadOnlyList<string> All = [Transitive, Hierarchical];

    /// <summary>Vrai si <paramref name="mode"/> appartient au vocabulaire fermé (jamais deviner, règle 2).</summary>
    public static bool IsValid(string? mode) =>
        mode is not null && (string.Equals(mode, Transitive, StringComparison.Ordinal)
            || string.Equals(mode, Hierarchical, StringComparison.Ordinal));
}
