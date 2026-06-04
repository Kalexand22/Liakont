namespace Liakont.Modules.Ingestion.Application;

/// <summary>
/// Options de stockage de l'ingestion (PIV04). Le chemin racine des PDF est un PARAMÉTRAGE de
/// déploiement (jamais une donnée client en dur — CLAUDE.md n°7) : il est fixé par l'hôte selon
/// l'environnement. Vide par défaut ; l'hôte (composition root) fournit une valeur effective.
/// </summary>
public sealed class IngestionStorageOptions
{
    /// <summary>Section de configuration (<c>Ingestion:Storage</c>).</summary>
    public const string SectionName = "Ingestion:Storage";

    /// <summary>Racine absolue du stockage des PDF reçus. Sous-arborescence par tenant (voir ADR-0008).</summary>
    public string PdfRootPath { get; set; } = string.Empty;
}
