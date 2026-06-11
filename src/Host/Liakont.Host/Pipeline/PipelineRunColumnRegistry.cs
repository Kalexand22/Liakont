namespace Liakont.Host.Pipeline;

using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;

/// <summary>
/// Registre de colonnes déclaratif de la page Traitements (WEB04a). Suit le gabarit des annuaires
/// admin (<c>AgentColumnRegistry</c>) : aucune grille maison, <c>DeclaredListPage</c> dérive de ce
/// registre les colonnes visibles, le sélecteur de colonnes, la recherche plein-texte et le filtre
/// avancé. Chaque clé de colonne correspond à une propriété de <see cref="PipelineRunRow"/> (résolution
/// réflexive pour le tri/la recherche). Le rendu spécifique (badges, date FR) est porté par les
/// <c>ColumnTemplates</c> de la page, pas ici.
/// </summary>
internal sealed class PipelineRunColumnRegistry : ColumnRegistryBase<PipelineRunRow>
{
    /// <summary>Table logique des exécutions (sert de catégorie/source aux colonnes).</summary>
    private const string SourceTable = "PipelineRun";

    /// <inheritdoc />
    protected override void Configure()
    {
        Column(nameof(PipelineRunRow.StartedAt), "Date", SourceTable, ColumnDataType.Date, defaultVisible: true, sortOrder: 0);
        Column(nameof(PipelineRunRow.Nature), "Nature", SourceTable, ColumnDataType.Text, defaultVisible: true, sortOrder: 1);
        Column(nameof(PipelineRunRow.Trigger), "Déclencheur", SourceTable, ColumnDataType.Text, defaultVisible: true, sortOrder: 2);
        Column(nameof(PipelineRunRow.Duration), "Durée", SourceTable, ColumnDataType.Text, defaultVisible: true, sortOrder: 3);
        Column(nameof(PipelineRunRow.DocumentsProcessed), "Traités", SourceTable, ColumnDataType.Number, defaultVisible: true, sortOrder: 4);
        Column(nameof(PipelineRunRow.DocumentsValidated), "Validés", SourceTable, ColumnDataType.Number, defaultVisible: true, sortOrder: 5);
        Column(nameof(PipelineRunRow.DocumentsFailed), "En échec", SourceTable, ColumnDataType.Number, defaultVisible: true, sortOrder: 6);

        // Détail VISIBLE par défaut (FIX05) : il porte le MOTIF opérateur d'un run qui n'a rien envoyé
        // (« aucun compte Plateforme Agréée actif… ») écrit par SendTenantJob. Masqué, un run sans envoi
        // affichait 0/0/0 et « ressemblait à un succès » ; le motif n'apparaissait que dans les logs serveur.
        Column(nameof(PipelineRunRow.Detail), "Détail", SourceTable, ColumnDataType.Text, defaultVisible: true, sortOrder: 7);
    }
}
