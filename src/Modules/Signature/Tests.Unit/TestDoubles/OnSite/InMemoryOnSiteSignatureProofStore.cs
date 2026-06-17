namespace Liakont.Modules.Signature.Tests.Unit.TestDoubles.OnSite;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Signature.Application.OnSite;

/// <summary>Journal de preuve en mémoire (enregistre les preuves consignées).</summary>
internal sealed class InMemoryOnSiteSignatureProofStore : IOnSiteSignatureProofStore
{
    public List<OnSiteSignatureProofRecord> Appended { get; } = [];

    public Task AppendAsync(OnSiteSignatureProofRecord record, CancellationToken cancellationToken = default)
    {
        Appended.Add(record);
        return Task.CompletedTask;
    }

    public Task<OnSiteSignatureProofRecord?> FindLatestAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
