namespace Liakont.Modules.Archive.Tests.Unit;

using System.Text;
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
    public async Task Read_Missing_ThrowsNotFound()
    {
        S3ArchiveStore store = CreateStore(new FakeS3BlobClient(), objectLock: false);
        Func<Task> read = () => store.ReadAsync("acme", "2026/05/F-1/missing.json");
        await read.Should().ThrowAsync<ArchiveObjectNotFoundException>();
    }
}
