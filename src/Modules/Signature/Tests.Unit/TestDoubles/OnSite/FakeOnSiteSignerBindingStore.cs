namespace Liakont.Modules.Signature.Tests.Unit.TestDoubles.OnSite;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Signature.Application.OnSite;

/// <summary>Registre de signataires vérifiés en mémoire : <see cref="ResolveVerifiedAsync"/> renvoie la liaison configurée.</summary>
internal sealed class FakeOnSiteSignerBindingStore : IOnSiteSignerBindingStore
{
    private readonly OnSiteSignerBindingRecord? _resolved;

    public FakeOnSiteSignerBindingStore(OnSiteSignerBindingRecord? resolved) => _resolved = resolved;

    public List<OnSiteSignerBindingRecord> Registered { get; } = [];

    public Task RegisterAsync(OnSiteSignerBindingRecord record, CancellationToken cancellationToken = default)
    {
        Registered.Add(record);
        return Task.CompletedTask;
    }

    public Task<OnSiteSignerBindingRecord?> ResolveVerifiedAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_resolved);
}
