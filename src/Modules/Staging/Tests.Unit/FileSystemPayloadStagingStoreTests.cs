namespace Liakont.Modules.Staging.Tests.Unit;

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Contracts.Serialization;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.Staging.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class FileSystemPayloadStagingStoreTests : IDisposable
{
    private const string ZeroHash = "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly string _root;
    private readonly FileSystemPayloadStagingStore _store;

    public FileSystemPayloadStagingStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "liakont-staging-tests-" + Guid.NewGuid().ToString("N"));
        _store = new FileSystemPayloadStagingStore(
            Options.Create(new FileSystemPayloadStagingStoreOptions { RootPath = _root }),
            BuildDataProtectionProvider());
    }

    private static IDataProtectionProvider BuildDataProtectionProvider()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    private static (StagedPayloadKey Key, string Json) Sample(string tenant = "tenant-a", decimal net = 100.00m)
    {
        var pivot = new PivotDocumentDto(
            sourceDocumentKind: "invoice",
            number: "INV-001",
            issueDate: new DateTime(2026, 6, 5),
            sourceReference: "ref-1",
            supplier: new PivotPartyDto("Fournisseur SARL"),
            totals: new PivotTotalsDto(net, 20.00m, net + 20.00m),
            operationCategory: OperationCategory.LivraisonBiens,
            lines: new[] { new PivotLineDto("Ligne A", net) });

        string json = CanonicalJson.Serialize(pivot);
        var key = new StagedPayloadKey(tenant, Guid.NewGuid(), PayloadHasher.ComputeHash(json));
        return (key, json);
    }

    private static string[] StagedFiles(string root) =>
        Directory.Exists(root)
            ? Directory.GetFiles(root, "*" + StagingPathLayout.PayloadFileExtension, SearchOption.AllDirectories)
            : Array.Empty<string>();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Write_Then_Read_Restitue_Le_Meme_Json_Canonique() // INV-STAGING-001
    {
        var (key, json) = Sample(net: 1234.50m);

        await _store.WriteAsync(key, json);
        string read = await _store.ReadAsync(key);

        read.Should().Be(json, "le round-trip de staging restitue le JSON canonique fidèlement (ADR-0014)");
        read.Should().Contain("1234.5", "les montants decimal sont préservés (échelle canonique ADR-0007)");
    }

    [Fact]
    public async Task Le_Contenu_Est_Chiffre_Au_Repos() // INV-STAGING-002
    {
        var (key, json) = Sample();

        await _store.WriteAsync(key, json);

        string[] files = StagedFiles(_root);
        files.Should().HaveCount(1);
        string rawOnDisk = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(files[0]));
        rawOnDisk.Should().NotContain("Fournisseur SARL", "le contenu fiscal est chiffré au repos (INV-STAGING-002)");
        rawOnDisk.Should().NotContain("INV-001");
    }

    [Fact]
    public async Task Read_Avec_Empreinte_Attendue_Differente_Rejette_En_Integrite() // INV-STAGING-003
    {
        var (key, json) = Sample();
        await _store.WriteAsync(key, json);

        // Même tenant + document (donc même fichier, déchiffrable) mais empreinte attendue erronée.
        var wrongHashKey = new StagedPayloadKey(key.TenantId, key.DocumentId, ZeroHash);
        Func<Task> act = () => _store.ReadAsync(wrongHashKey);

        await act.Should().ThrowAsync<StagedPayloadIntegrityException>();
    }

    [Fact]
    public async Task Read_Blob_Corrompu_Rejette_En_Integrite() // INV-STAGING-003
    {
        var (key, json) = Sample();
        await _store.WriteAsync(key, json);

        string[] files = StagedFiles(_root);
        byte[] garbage = [1, 2, 3, 4, 5];
        await File.WriteAllBytesAsync(files[0], garbage);

        Func<Task> act = () => _store.ReadAsync(key);
        await act.Should().ThrowAsync<StagedPayloadIntegrityException>();
    }

    [Fact]
    public async Task Read_Entree_Absente_Est_Transitoire_Pas_Terminale() // INV-STAGING-004
    {
        var key = new StagedPayloadKey("tenant-a", Guid.NewGuid(), ZeroHash);

        Func<Task> act = () => _store.ReadAsync(key);

        await act.Should().ThrowAsync<StagedPayloadNotFoundException>();
    }

    [Fact]
    public async Task Isolation_Tenant_Un_Tenant_Ne_Lit_Pas_Le_Staging_D_Un_Autre() // INV-STAGING-005
    {
        var (keyA, json) = Sample(tenant: "tenant-a");
        await _store.WriteAsync(keyA, json);

        // Même document_id et même empreinte, mais un AUTRE tenant : aucune entrée visible.
        var keyB = new StagedPayloadKey("tenant-b", keyA.DocumentId, keyA.PayloadHash);

        (await _store.ExistsAsync(keyB)).Should().BeFalse("un tenant ne voit jamais le staging d'un autre (INV-STAGING-005)");
        (await _store.ExistsAsync(keyA)).Should().BeTrue();
        Func<Task> act = () => _store.ReadAsync(keyB);
        await act.Should().ThrowAsync<StagedPayloadNotFoundException>();
    }

    [Fact]
    public async Task Purge_Supprime_L_Entree_Et_Est_Idempotente() // INV-STAGING-006
    {
        var (key, json) = Sample();
        await _store.WriteAsync(key, json);
        (await _store.ExistsAsync(key)).Should().BeTrue();

        await _store.PurgeAsync(key);
        (await _store.ExistsAsync(key)).Should().BeFalse();

        // Re-purge d'une entrée déjà absente = no-op (jamais d'erreur).
        await _store.PurgeAsync(key);
        (await _store.ExistsAsync(key)).Should().BeFalse();
    }

    [Fact]
    public async Task Re_ecrire_Le_Meme_Contenu_Est_Idempotent() // INV-STAGING-008
    {
        var (key, json) = Sample();

        await _store.WriteAsync(key, json);
        await _store.WriteAsync(key, json);

        StagedFiles(_root).Should().HaveCount(1, "une seule entrée pour la clé (idempotent)");
        (await _store.ReadAsync(key)).Should().Be(json);
    }

    [Fact]
    public void Les_Capacites_Sont_None() // INV-STAGING-009
    {
        _store.Capabilities.Should().Be(PayloadStagingStoreCapabilities.None);
    }
}
