namespace Liakont.Modules.Archive.Tests.Unit;

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Archive.Infrastructure;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class FileSystemArchiveStoreTests : IDisposable
{
    private readonly string _root;
    private readonly FileSystemArchiveStore _store;

    public FileSystemArchiveStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "liakont-archive-tests", Guid.NewGuid().ToString("N"));
        _store = new FileSystemArchiveStore(Options.Create(new FileSystemArchiveStoreOptions { RootPath = _root }));
    }

    [Fact]
    public void Capabilities_AreNone_IntegrityFromHashChainOnly()
    {
        _store.Capabilities.Should().Be(ArchiveStoreCapabilities.None);
    }

    [Fact]
    public async Task Write_Then_Read_RoundTrips()
    {
        byte[] content = Encoding.UTF8.GetBytes("paquet");
        await _store.WriteAsync("acme", "2026/05/F-1/payload.json", content);

        (await _store.ReadAsync("acme", "2026/05/F-1/payload.json")).Should().Equal(content);
        (await _store.ExistsAsync("acme", "2026/05/F-1/payload.json")).Should().BeTrue();
    }

    [Fact]
    public async Task Write_SameContentTwice_IsIdempotent()
    {
        byte[] content = Encoding.UTF8.GetBytes("paquet");
        await _store.WriteAsync("acme", "2026/05/F-1/payload.json", content);
        Func<Task> again = () => _store.WriteAsync("acme", "2026/05/F-1/payload.json", content);
        await again.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Write_DifferentContentSamePath_ThrowsWormConflict()
    {
        await _store.WriteAsync("acme", "2026/05/F-1/payload.json", Encoding.UTF8.GetBytes("original"));
        Func<Task> overwrite = () => _store.WriteAsync("acme", "2026/05/F-1/payload.json", Encoding.UTF8.GetBytes("altéré"));
        await overwrite.Should().ThrowAsync<ArchiveWriteConflictException>();
    }

    [Fact]
    public async Task Read_Missing_ThrowsNotFound()
    {
        Func<Task> read = () => _store.ReadAsync("acme", "2026/05/F-1/missing.json");
        await read.Should().ThrowAsync<ArchiveObjectNotFoundException>();
    }

    [Fact]
    public async Task Write_PathTraversal_IsRejected()
    {
        Func<Task> traversal = () => _store.WriteAsync("acme", "../escape.json", Encoding.UTF8.GetBytes("x"));
        await traversal.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }
}
