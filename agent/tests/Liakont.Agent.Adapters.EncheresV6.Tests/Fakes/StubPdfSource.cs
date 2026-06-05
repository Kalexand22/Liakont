namespace Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;

using System;
using System.Collections.Generic;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Core.Extraction;

/// <summary>
/// Source PDF de test à résultats canoniques : prouve que l'extracteur (Fixture / Pervasive) DÉLÈGUE
/// bien <see cref="IExtractor.GetAttachments"/> / <see cref="IExtractor.ListPoolDocuments"/> et reflète
/// les capacités déclarées de la source — sans dépendre du système de fichiers réel.
/// </summary>
public sealed class StubPdfSource : IEncheresV6PdfSource
{
    private readonly IReadOnlyList<SourceAttachment> _attachments;
    private readonly IReadOnlyList<PoolDocument> _pool;

    public StubPdfSource(
        bool providesSourceDocuments,
        bool providesUnlinkedDocumentPool,
        IReadOnlyList<SourceAttachment>? attachments = null,
        IReadOnlyList<PoolDocument>? pool = null)
    {
        ProvidesSourceDocuments = providesSourceDocuments;
        ProvidesUnlinkedDocumentPool = providesUnlinkedDocumentPool;
        _attachments = attachments ?? Array.Empty<SourceAttachment>();
        _pool = pool ?? Array.Empty<PoolDocument>();
    }

    /// <summary>Dernière référence reçue par <see cref="GetAttachments"/> (preuve de délégation).</summary>
    public string? LastRequestedReference { get; private set; }

    /// <inheritdoc />
    public bool ProvidesSourceDocuments { get; }

    /// <inheritdoc />
    public bool ProvidesUnlinkedDocumentPool { get; }

    /// <inheritdoc />
    public IReadOnlyList<SourceAttachment> GetAttachments(string sourceReference)
    {
        LastRequestedReference = sourceReference;
        return _attachments;
    }

    /// <inheritdoc />
    public IEnumerable<PoolDocument> ListPoolDocuments(DateTime fromInclusiveUtc, DateTime toExclusiveUtc) => _pool;
}
