namespace Liakont.Agent.Core.Extraction;

using Liakont.Agent.Contracts.Transport;

/// <summary>
/// Projette les capacités déclarées d'un extracteur (<see cref="ExtractorCapabilities"/>) vers le DTO de
/// transport (<see cref="ExtractorCapabilitiesDto"/>, ADR-0004 D2 / RD401). L'agent ne fait que
/// DÉCLARER : les formes énumérées voyagent en valeur BRUTE (nom de l'énumération source), jamais
/// interprétées (CLAUDE.md n°6). L'interprétation appartient aux consommateurs plateforme (RD403/RD409).
/// </summary>
public static class ExtractorCapabilitiesMapper
{
    /// <summary>Projette les capacités déclarées vers le DTO de transport.</summary>
    /// <param name="capabilities">Capacités déclarées par l'extracteur.</param>
    /// <returns>Le DTO de transport correspondant.</returns>
    public static ExtractorCapabilitiesDto ToDto(ExtractorCapabilities capabilities)
    {
        return new ExtractorCapabilitiesDto(
            providesSourceDocuments: capabilities.ProvidesSourceDocuments,
            providesUnlinkedDocumentPool: capabilities.ProvidesUnlinkedDocumentPool,
            hasDetailedLines: capabilities.HasDetailedLines,
            hasCreditNoteLink: capabilities.HasCreditNoteLink,
            exposesPayments: capabilities.ExposesPayments,
            regimeKeyShape: capabilities.RegimeKeyShape.ToString(),
            emitterIdentitySource: capabilities.EmitterIdentitySource.ToString(),
            hasStoredHeaderTotal: capabilities.HasStoredHeaderTotal,
            isMutableAfterIssue: capabilities.IsMutableAfterIssue,
            numberUniquenessScope: capabilities.NumberUniquenessScope.ToString());
    }
}
