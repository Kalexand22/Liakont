namespace Liakont.Host.Tests.Unit.InstanceEmail;

using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.FleetSupervision.Application;

/// <summary>
/// Magasin de config email d'instance en mémoire (ADR-0039) : sert d'état pour tester la précédence
/// DB-vs-appsettings du transport, le lit-puis-conserve du service, etc. Ne manipule que du ciphertext.
/// </summary>
internal sealed class FakeInstanceEmailConfigStore : IInstanceEmailConfigStore
{
    public InstanceEmailConfig? Current { get; set; }

    public InstanceEmailConfig? LastUpserted { get; private set; }

    public int UpsertCount { get; private set; }

    public Task<InstanceEmailConfig?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Current);

    public Task UpsertAsync(InstanceEmailConfig config, CancellationToken cancellationToken = default)
    {
        LastUpserted = config;
        Current = config;
        UpsertCount++;
        return Task.CompletedTask;
    }
}
