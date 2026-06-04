namespace Liakont.Modules.Archive.Tests.Unit.Doubles;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Infrastructure;

/// <summary>
/// Double de <see cref="ITsaClient"/> : délègue l'émission de la réponse à une fonction (typiquement une
/// <see cref="TestTimestampAuthority"/>), sans appel réseau. Compte les appels (idempotence du job).
/// </summary>
internal sealed class FakeTsaClient : ITsaClient
{
    private readonly Func<byte[], byte[]> _issue;

    public FakeTsaClient(Func<byte[], byte[]> issue)
    {
        _issue = issue;
    }

    public int CallCount { get; private set; }

    public static FakeTsaClient Backed(TestTimestampAuthority authority) => new(authority.IssueResponse);

    public Task<byte[]> RequestTokenAsync(byte[] requestDer, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(_issue(requestDer));
    }
}
