namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using System.Collections.Generic;
using Liakont.Agent.Core.Extraction;

/// <summary>
/// Source PDF NEUTRE (null-object) d'EncheresV6 : aucune capacité déclarée, résultats toujours vides,
/// jamais d'exception. C'est le défaut d'un extracteur dont la configuration ne déclare AUCUNE source
/// PDF (ni dossier lié, ni pool) — l'extracteur conserve alors le comportement « pas de PDF »
/// (capacités false, listes vides) sans aucune branche conditionnelle. Utiliser <see cref="Instance"/>.
/// </summary>
public sealed class NullEncheresV6PdfSource : IEncheresV6PdfSource
{
    /// <summary>Instance partagée (immuable, sans état) du null-object.</summary>
    public static readonly NullEncheresV6PdfSource Instance = new NullEncheresV6PdfSource();

    private NullEncheresV6PdfSource()
    {
    }

    /// <inheritdoc />
    public bool ProvidesSourceDocuments => false;

    /// <inheritdoc />
    public bool ProvidesUnlinkedDocumentPool => false;

    /// <inheritdoc />
    public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference) =>
        Array.Empty<SourceAttachment>();

    /// <inheritdoc />
    public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) =>
        Array.Empty<PoolDocument>();
}
