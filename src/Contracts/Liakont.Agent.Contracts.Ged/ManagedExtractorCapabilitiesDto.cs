namespace Liakont.Agent.Contracts.Ged;

/// <summary>
/// Capacités DÉCLARÉES d'un extracteur GED, transportées de l'agent vers la plateforme (F19 §4.6) —
/// DTO PUR, DISJOINT d'<c>ExtractorCapabilitiesDto</c> (le canal fiscal). L'agent DÉCLARE ce que SA
/// source sait fournir pour la GED ; la plateforme s'y adapte, JAMAIS par <c>if (source is …)</c>
/// (ADR-0004 D2, CLAUDE.md n°8). Tous les paramètres ont une valeur par défaut SÛRE (capacité absente).
/// </summary>
public sealed class ManagedExtractorCapabilitiesDto
{
    /// <summary>Crée un jeu de capacités GED transporté. Défauts : toutes capacités absentes (<c>false</c>).</summary>
    /// <param name="providesManagedDocuments">La source fournit des documents GÉRÉS non-facture (canal GED).</param>
    /// <param name="providesAxes">La source porte des indices d'axes (métadonnées de classement).</param>
    /// <param name="providesEntities">La source porte des indices d'entités (acteurs, objets métier).</param>
    /// <param name="providesRelations">La source porte des indices de relations entre document et entités.</param>
    /// <param name="providesBinaryContent">La source expose un contenu binaire (PDF, image…) rattaché.</param>
    public ManagedExtractorCapabilitiesDto(
        bool providesManagedDocuments = false,
        bool providesAxes = false,
        bool providesEntities = false,
        bool providesRelations = false,
        bool providesBinaryContent = false)
    {
        ProvidesManagedDocuments = providesManagedDocuments;
        ProvidesAxes = providesAxes;
        ProvidesEntities = providesEntities;
        ProvidesRelations = providesRelations;
        ProvidesBinaryContent = providesBinaryContent;
    }

    /// <summary>La source fournit des documents gérés non-facture (canal GED).</summary>
    public bool ProvidesManagedDocuments { get; }

    /// <summary>La source porte des indices d'axes.</summary>
    public bool ProvidesAxes { get; }

    /// <summary>La source porte des indices d'entités.</summary>
    public bool ProvidesEntities { get; }

    /// <summary>La source porte des indices de relations.</summary>
    public bool ProvidesRelations { get; }

    /// <summary>La source expose un contenu binaire rattaché.</summary>
    public bool ProvidesBinaryContent { get; }
}
