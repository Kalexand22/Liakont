namespace Liakont.Modules.TenantSettings.Contracts.Commands;

using Stratum.Common.Abstractions.Messaging;

/// <summary>
/// Remplace EN BLOC la matrice de routage des alertes du tenant courant (F12 §5.3.1, FIX212). La liste
/// fournie devient l'intégralité de la matrice (replace-all) ; une liste vide efface la matrice et
/// rétablit le modèle simple par défaut. Chaque entrée est validée par le domaine
/// (<c>AlertRoutingRule.Create</c>) ; la mutation est journalisée (piste append-only) sans exposer les
/// adresses. Table de PARAMÉTRAGE mutable (≠ piste d'audit) — CLAUDE.md n°4 ne s'applique pas.
/// </summary>
public record SetAlertRoutingMatrixCommand : ICommand
{
    /// <summary>Entrées de la matrice, dans l'ordre voulu (le rang est dérivé de la position).</summary>
    public required IReadOnlyList<AlertRoutingRuleInput> Rules { get; init; }
}
