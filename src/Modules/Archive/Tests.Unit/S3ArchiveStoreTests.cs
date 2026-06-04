namespace Liakont.Modules.Archive.Tests.Unit;

using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Stores.S3;
using Liakont.Modules.Archive.Tests.Unit.Doubles;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class S3ArchiveStoreTests
{
    private static S3ArchiveStore CreateStore(FakeS3BlobClient client, bool objectLock) =>
        new(client, Options.Create(new S3ArchiveStoreOptions
        {
            BucketName = "coffre",
            SupportsObjectLock = objectLock,
            SupportsLegalHold = objectLock,
        }));

    [Fact]
    public async Task Write_MapsTenantAndPathToKey_AndRoundTrips()
    {
        var client = new FakeS3BlobClient();
        S3ArchiveStore store = CreateStore(client, objectLock: false);

        byte[] content = Encoding.UTF8.GetBytes("paquet");
        await store.WriteAsync("acme", "2026/05/F-1/payload.json", content);

        client.ObjectLockApplied.Should().ContainKey("acme/2026/05/F-1/payload.json");
        (await store.ReadAsync("acme", "2026/05/F-1/payload.json")).Should().Equal(content);
    }

    [Fact]
    public async Task Write_AppliesObjectLock_WhenCapabilityDeclared()
    {
        var client = new FakeS3BlobClient();
        S3ArchiveStore store = CreateStore(client, objectLock: true);

        await store.WriteAsync("acme", "2026/05/F-1/payload.json", Encoding.UTF8.GetBytes("x"));

        store.Capabilities.SupportsObjectLock.Should().BeTrue();
        client.ObjectLockApplied["acme/2026/05/F-1/payload.json"].Should().BeTrue();
    }

    [Fact]
    public async Task Write_DoesNotApplyObjectLock_WhenCapabilityAbsent()
    {
        var client = new FakeS3BlobClient();
        S3ArchiveStore store = CreateStore(client, objectLock: false);

        await store.WriteAsync("acme", "2026/05/F-1/payload.json", Encoding.UTF8.GetBytes("x"));

        store.Capabilities.SupportsObjectLock.Should().BeFalse();
        client.ObjectLockApplied["acme/2026/05/F-1/payload.json"].Should().BeFalse();
    }

    [Fact]
    public async Task Write_DifferentContentSameKey_ThrowsWormConflict()
    {
        var client = new FakeS3BlobClient();
        S3ArchiveStore store = CreateStore(client, objectLock: false);
        await store.WriteAsync("acme", "2026/05/F-1/payload.json", Encoding.UTF8.GetBytes("original"));

        Func<Task> overwrite = () => store.WriteAsync("acme", "2026/05/F-1/payload.json", Encoding.UTF8.GetBytes("altéré"));
        await overwrite.Should().ThrowAsync<ArchiveWriteConflictException>();
    }

    [Fact]
    public async Task Write_SameContentTwice_IsIdempotent()
    {
        var client = new FakeS3BlobClient();
        S3ArchiveStore store = CreateStore(client, objectLock: false);
        byte[] content = Encoding.UTF8.GetBytes("paquet");

        await store.WriteAsync("acme", "2026/05/F-1/payload.json", content);

        // Réécriture du MÊME contenu : la création atomique est refusée (objet déjà présent), la
        // relecture confirme l'identité → idempotent, aucune exception, contenu inchangé.
        Func<Task> rewrite = () => store.WriteAsync("acme", "2026/05/F-1/payload.json", content);
        await rewrite.Should().NotThrowAsync();
        (await store.ReadAsync("acme", "2026/05/F-1/payload.json")).Should().Equal(content);
    }

    [Fact]
    public async Task Read_Missing_ThrowsNotFound()
    {
        S3ArchiveStore store = CreateStore(new FakeS3BlobClient(), objectLock: false);
        Func<Task> read = () => store.ReadAsync("acme", "2026/05/F-1/missing.json");
        await read.Should().ThrowAsync<ArchiveObjectNotFoundException>();
    }

    // ── Branches de course (création atomique refusée) : un écrivain concurrent a gagné la clé entre
    //    l'arbitrage initial et la création atomique. Le store rejoue alors l'arbitrage conflit/idempotence.

    [Fact]
    public async Task Write_RaceLost_ConcurrentWroteSameContent_IsIdempotent()
    {
        byte[] content = Encoding.UTF8.GetBytes("paquet");

        // Clé vue libre, puis création refusée (course perdue), puis relecture = MÊME contenu → idempotent.
        var client = new ScriptedS3BlobClient(putIfAbsentResult: false, getResults: new byte[]?[] { null, content });
        S3ArchiveStore store = new(client, Options.Create(new S3ArchiveStoreOptions { BucketName = "coffre" }));

        Func<Task> write = () => store.WriteAsync("acme", "2026/05/F-1/payload.json", content);

        await write.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Write_RaceLost_ConcurrentWroteDifferentContent_ThrowsWormConflict()
    {
        var client = new ScriptedS3BlobClient(
            putIfAbsentResult: false,
            getResults: new byte[]?[] { null, Encoding.UTF8.GetBytes("autre") });
        S3ArchiveStore store = new(client, Options.Create(new S3ArchiveStoreOptions { BucketName = "coffre" }));

        Func<Task> write = () => store.WriteAsync("acme", "2026/05/F-1/payload.json", Encoding.UTF8.GetBytes("paquet"));

        await write.Should().ThrowAsync<ArchiveWriteConflictException>();
    }

    [Fact]
    public async Task Write_CreateRefusedButObjectAbsent_ThrowsWormConflict()
    {
        // Cas pathologique : création atomique refusée MAIS objet introuvable à la relecture (disparition
        // interdite en WORM). On lève plutôt que de réécrire en silence.
        var client = new ScriptedS3BlobClient(putIfAbsentResult: false, getResults: new byte[]?[] { null, null });
        S3ArchiveStore store = new(client, Options.Create(new S3ArchiveStoreOptions { BucketName = "coffre" }));

        Func<Task> write = () => store.WriteAsync("acme", "2026/05/F-1/payload.json", Encoding.UTF8.GetBytes("paquet"));

        await write.Should().ThrowAsync<ArchiveWriteConflictException>();
    }

    /// <summary>
    /// Double <see cref="IS3BlobClient"/> à réponses SCRIPTÉES : permet de simuler une course que le double
    /// cohérent <see cref="FakeS3BlobClient"/> ne peut pas reproduire (création atomique refusée alors que
    /// la clé était vue libre). <c>TryGetAsync</c> consomme <paramref name="getResults"/> dans l'ordre.
    /// </summary>
    private sealed class ScriptedS3BlobClient : IS3BlobClient
    {
        private readonly bool _putIfAbsentResult;
        private readonly Queue<byte[]?> _getResults;

        public ScriptedS3BlobClient(bool putIfAbsentResult, byte[]?[] getResults)
        {
            _putIfAbsentResult = putIfAbsentResult;
            _getResults = new Queue<byte[]?>(getResults);
        }

        public Task<bool> TryPutIfAbsentAsync(string key, byte[] content, bool applyObjectLock, CancellationToken cancellationToken) =>
            Task.FromResult(_putIfAbsentResult);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<byte[]?> TryGetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(_getResults.Count > 0 ? _getResults.Dequeue() : null);
    }
}
