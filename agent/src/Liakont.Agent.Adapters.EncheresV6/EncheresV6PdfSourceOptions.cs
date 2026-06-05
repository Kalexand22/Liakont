namespace Liakont.Agent.Adapters.EncheresV6;

using System;

/// <summary>
/// Configuration TYPÉE de la source PDF « dossier de fichiers » d'EncheresV6 (ADP05), consommée par
/// <see cref="FileSystemEncheresV6PdfSource"/>. C'est un sous-ensemble du paramétrage de l'adaptateur
/// (section PDF) : où trouver les bordereaux PDF sur le serveur, et selon quel mode.
/// <para>
/// Deux modes INDÉPENDANTS, qui peuvent coexister (acceptance ADP05) :
/// </para>
/// <list type="bullet">
///   <item><b>Lié</b> (<see cref="LinkedFolderPath"/> renseigné) : un dossier où le NOM de fichier
///   contient le <c>no_ba</c> du bordereau — <see cref="FileSystemEncheresV6PdfSource.GetAttachments"/>
///   retrouve le(s) PDF par la référence source du document.</item>
///   <item><b>Pool</b> (<see cref="PoolFolderPath"/> renseigné) : un dossier de PDF en VRAC, sans lien
///   fiable — <see cref="FileSystemEncheresV6PdfSource.ListPoolDocuments"/> les expose, la
///   réconciliation se fait côté plateforme (TRK07).</item>
/// </list>
/// <para>
/// Ce n'est PAS une donnée client (CLAUDE.md n°7) : les CHEMINS réels (dossier du serveur du tenant)
/// sont du paramétrage de déploiement (lot CMP / <c>agent.json</c>), pas du code. Cette classe ne
/// porte que la FORME du paramétrage (un produit). Aucune valeur n'est embarquée.
/// </para>
/// </summary>
public sealed class EncheresV6PdfSourceOptions
{
    /// <summary>Motif de recherche des fichiers PDF par défaut (extension PDF, insensible à la casse via le système de fichiers).</summary>
    public const string DefaultSearchPattern = "*.pdf";

    /// <summary>Crée une configuration de source PDF « dossier de fichiers ».</summary>
    /// <param name="linkedFolderPath">
    /// Dossier des PDF LIÉS (nom de fichier contenant le <c>no_ba</c>). <c>null</c>/vide ⇒ mode lié désactivé
    /// (capacité <see cref="IEncheresV6PdfSource.ProvidesSourceDocuments"/> non déclarée).
    /// </param>
    /// <param name="poolFolderPath">
    /// Dossier des PDF en POOL (vrac non lié). <c>null</c>/vide ⇒ mode pool désactivé (capacité
    /// <see cref="IEncheresV6PdfSource.ProvidesUnlinkedDocumentPool"/> non déclarée).
    /// </param>
    /// <param name="searchPattern">Motif de recherche des fichiers (défaut <see cref="DefaultSearchPattern"/>).</param>
    /// <exception cref="ArgumentException">Si <paramref name="searchPattern"/> est vide.</exception>
    public EncheresV6PdfSourceOptions(
        string? linkedFolderPath = null,
        string? poolFolderPath = null,
        string searchPattern = DefaultSearchPattern)
    {
        if (string.IsNullOrWhiteSpace(searchPattern))
        {
            throw new ArgumentException(
                "Le motif de recherche des PDF EncheresV6 est requis (par défaut « *.pdf »).",
                nameof(searchPattern));
        }

        // Normalisation : un chemin vide ou en blanc équivaut à « mode désactivé » (null), pour qu'une
        // section de config présente mais non renseignée ne déclare pas une capacité non honorée.
        LinkedFolderPath = string.IsNullOrWhiteSpace(linkedFolderPath) ? null : linkedFolderPath!.Trim();
        PoolFolderPath = string.IsNullOrWhiteSpace(poolFolderPath) ? null : poolFolderPath!.Trim();
        SearchPattern = searchPattern;
    }

    /// <summary>Dossier des PDF liés (mode lié), ou <c>null</c> si le mode lié est désactivé.</summary>
    public string? LinkedFolderPath { get; }

    /// <summary>Dossier des PDF en pool (mode pool), ou <c>null</c> si le mode pool est désactivé.</summary>
    public string? PoolFolderPath { get; }

    /// <summary>Motif de recherche des fichiers PDF (ex. « *.pdf »).</summary>
    public string SearchPattern { get; }

    /// <summary>Le mode lié est-il activé (un dossier de PDF liés est configuré) ?</summary>
    public bool LinkedModeEnabled => LinkedFolderPath != null;

    /// <summary>Le mode pool est-il activé (un dossier de PDF en pool est configuré) ?</summary>
    public bool PoolModeEnabled => PoolFolderPath != null;
}
