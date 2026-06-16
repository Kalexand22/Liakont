namespace Liakont.Modules.SupportTrace.Tests.Unit;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.SupportTrace.Contracts;
using Liakont.Modules.SupportTrace.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Comportement du store de trace de support FileSystem (FX06) : round-trip chiffré, lecture de la copie la
/// plus récente, purge bornée à la rétention, isolation tenant et chiffrement au repos. Aucune base : store de
/// fichiers, vérifié en direct sur un répertoire temporaire.
/// </summary>
public sealed class FileSystemSupportTraceStoreTests : IDisposable
{
    private static readonly DateTimeOffset Day1 = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 = new(2026, 3, 5, 9, 30, 0, TimeSpan.Zero);

    private readonly string _root;
    private readonly FileSystemSupportTraceStore _store;

    public FileSystemSupportTraceStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "liakont-supporttrace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _store = new FileSystemSupportTraceStore(
            Options.Create(new SupportTraceOptions { RootPath = _root, RetentionDays = 90 }),
            new EphemeralDataProtectionProvider());
    }

    [Fact]
    public async Task Write_Then_Read_RoundTrips()
    {
        var documentId = Guid.NewGuid();
        byte[] facturX = Encoding.UTF8.GetBytes("%PDF-FacturX-octets");

        await _store.WriteAsync("tenant-a", documentId, facturX, Day1);
        byte[]? read = await _store.ReadAsync("tenant-a", documentId);

        read.Should().NotBeNull();
        read!.Should().Equal(facturX);
    }

    [Fact]
    public async Task Read_Returns_Null_When_Absent()
    {
        (await _store.ReadAsync("tenant-a", Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Read_Returns_The_Most_Recent_Copy()
    {
        var documentId = Guid.NewGuid();
        await _store.WriteAsync("tenant-a", documentId, Encoding.UTF8.GetBytes("ancien"), Day1);
        await _store.WriteAsync("tenant-a", documentId, Encoding.UTF8.GetBytes("recent"), Day2);

        byte[]? read = await _store.ReadAsync("tenant-a", documentId);

        Encoding.UTF8.GetString(read!).Should().Be("recent");
    }

    [Fact]
    public async Task Purge_Removes_Only_Entries_Older_Than_Cutoff()
    {
        var oldDoc = Guid.NewGuid();
        var recentDoc = Guid.NewGuid();
        await _store.WriteAsync("tenant-a", oldDoc, Encoding.UTF8.GetBytes("vieux"), Day1);
        await _store.WriteAsync("tenant-a", recentDoc, Encoding.UTF8.GetBytes("neuf"), Day2);

        // Borne au 3 mars : le jour 1 (1 mars) est strictement antérieur → purgé ; le jour 5 est conservé.
        int purged = await _store.PurgeOlderThanAsync("tenant-a", new DateTimeOffset(2026, 3, 3, 0, 0, 0, TimeSpan.Zero));

        purged.Should().Be(1);
        (await _store.ReadAsync("tenant-a", oldDoc)).Should().BeNull("l'entrée expirée a été purgée");
        (await _store.ReadAsync("tenant-a", recentDoc)).Should().NotBeNull("l'entrée récente est conservée");
    }

    [Fact]
    public async Task Purge_Is_Tenant_Scoped()
    {
        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();
        await _store.WriteAsync("tenant-a", docA, Encoding.UTF8.GetBytes("a"), Day1);
        await _store.WriteAsync("tenant-b", docB, Encoding.UTF8.GetBytes("b"), Day1);

        int purged = await _store.PurgeOlderThanAsync("tenant-a", new DateTimeOffset(2026, 3, 3, 0, 0, 0, TimeSpan.Zero));

        purged.Should().Be(1);
        (await _store.ReadAsync("tenant-a", docA)).Should().BeNull("le tenant a purgé sa propre trace");
        (await _store.ReadAsync("tenant-b", docB)).Should().NotBeNull("la purge d'un tenant ne touche jamais un autre tenant");
    }

    [Fact]
    public async Task A_Tenant_Cannot_Read_Another_Tenants_Trace()
    {
        var documentId = Guid.NewGuid();
        await _store.WriteAsync("tenant-a", documentId, Encoding.UTF8.GetBytes("secret-a"), Day1);

        (await _store.ReadAsync("tenant-b", documentId)).Should().BeNull("la lecture est tenant-scopée");
    }

    [Fact]
    public async Task Stored_Bytes_Are_Encrypted_At_Rest()
    {
        var documentId = Guid.NewGuid();
        byte[] plaintext = Encoding.UTF8.GetBytes("DONNEE-FISCALE-EN-CLAIR");
        await _store.WriteAsync("tenant-a", documentId, plaintext, Day1);

        string traceFile = Directory.EnumerateFiles(_root, "*.fxtrace", SearchOption.AllDirectories).Single();
        byte[] onDisk = await File.ReadAllBytesAsync(traceFile);

        ContainsSubsequence(onDisk, plaintext)
            .Should().BeFalse("les octets fiscaux sont chiffrés au repos (CLAUDE.md n°10), jamais en clair sur le disque");
    }

    [Fact]
    public async Task Write_Rejects_An_Empty_Artifact()
    {
        var act = async () => await _store.WriteAsync("tenant-a", Guid.NewGuid(), ReadOnlyMemory<byte>.Empty, Day1);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static bool ContainsSubsequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
        {
            return false;
        }

        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.Skip(i).Take(needle.Length).SequenceEqual(needle))
            {
                return true;
            }
        }

        return false;
    }
}
