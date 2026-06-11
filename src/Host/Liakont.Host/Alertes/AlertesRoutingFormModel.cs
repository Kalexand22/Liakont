namespace Liakont.Host.Alertes;

using System.Collections.Generic;

/// <summary>
/// Saisie éditable de la matrice de routage des alertes (FIX212, F12 §5.3.1). EXTENSION du modèle simple :
/// une matrice vide laisse le routage par défaut (opérateur = toutes ; contact = critiques opt-in). Chaque
/// ligne cible une règle ou une gravité et liste ses destinataires (CSV). Mutable (liée aux champs du
/// formulaire), instance partagée avec la page (ajout/retrait de lignes).
/// </summary>
public sealed class AlertesRoutingFormModel
{
    /// <summary>Lignes de la matrice, dans l'ordre d'affichage (le rang est dérivé de la position à l'enregistrement).</summary>
    public List<AlertesRoutingRow> Rows { get; init; } = [];
}
