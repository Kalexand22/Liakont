namespace Liakont.Modules.Ged.Domain.Graph;

using System;

/// <summary>
/// Règle d'inférence/héritage déclarée par le tenant (F19 §10, GED24) : pour un <see cref="RelationKind"/> métier
/// DÉCLARÉ (paramétrage tenant, jamais en dur — règle 7 / INV-GED-12), un <see cref="Mode"/> de dérivation
/// (<see cref="RelationInferenceMode"/>) et une <see cref="MaxDepth"/> = borne de profondeur (paramètre tenant,
/// F19 §6.4 « jamais infinie »), plafonnée par la borne dure produit <see cref="MaxAllowedDepth"/> (anti-DoS).
/// Miroir Domain de <c>ged_catalog.relation_inference_rules</c>.
/// </summary>
public sealed class RelationInferenceRule
{
    /// <summary>
    /// Borne DURE produit de profondeur (anti-DoS, F19 §6.4) — miroir de <c>ck_rir_max_depth</c>. Ce n'est PAS
    /// une règle métier : c'est un plafond d'exploration du graphe qui garantit la terminaison.
    /// </summary>
    public const int MaxAllowedDepth = 8;

    public RelationInferenceRule(string relationKind, string mode, int maxDepth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relationKind);

        if (!RelationInferenceMode.IsValid(mode))
        {
            throw new ArgumentException(
                $"Mode d'inférence de relation GED « {mode} » invalide : attendu l'une de "
                    + $"[{string.Join(", ", RelationInferenceMode.All)}] (miroir ck_rir_mode, jamais deviner CLAUDE.md n.2).",
                nameof(mode));
        }

        if (maxDepth is < 1 or > MaxAllowedDepth)
        {
            var message = $"Borne de profondeur d'inférence GED ({maxDepth}) hors de l'intervalle [1..{MaxAllowedDepth}] "
                + "(borne anti-DoS, miroir ck_rir_max_depth / F19 §6.4).";
            throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, message);
        }

        RelationKind = relationKind;
        Mode = mode;
        MaxDepth = maxDepth;
    }

    /// <summary>Genre métier ciblé (<c>relation_kind</c>, paramétrage tenant).</summary>
    public string RelationKind { get; }

    /// <summary>Mode de dérivation (<see cref="RelationInferenceMode.Transitive"/> ou <see cref="RelationInferenceMode.Hierarchical"/>).</summary>
    public string Mode { get; }

    /// <summary>Borne de profondeur (paramètre tenant, [1..<see cref="MaxAllowedDepth"/>]).</summary>
    public int MaxDepth { get; }
}
