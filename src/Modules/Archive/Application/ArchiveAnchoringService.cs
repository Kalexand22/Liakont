namespace Liakont.Modules.Archive.Application;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Domain;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Ancre la tête de chaîne du coffre du tenant courant (TRK06). Tenant-scopé : coffre rooté sur le tenant
/// courant, base routée vers la base du tenant. Pilote par les <see cref="TimestampAnchorCapabilities"/>
/// de l'ancrage configuré (jamais un test de type). Ordre WORM : preuve écrite dans le coffre (write-once)
/// AVANT l'indexation en base, qui n'a lieu qu'une fois la preuve archivée.
/// </summary>
public sealed class ArchiveAnchoringService : IArchiveAnchoringService
{
    private const string AnchorNotice =
        "Preuve d'ancrage temporel Liakont. Elle atteste qu'à l'instant scellé, la tête de chaîne du coffre " +
        "(chain_hash) portait cette empreinte. Combinée à la chaîne de hashes, elle borne dans le temps toute " +
        "altération a posteriori. Ce coffre n'est pas un SAE certifié NF Z42-013.";

    private readonly IArchiveEntryStore _entryStore;
    private readonly IArchiveAnchorStore _anchorStore;
    private readonly IArchiveStore _store;
    private readonly ITimestampAnchor _anchor;
    private readonly ITenantContext _tenantContext;

    public ArchiveAnchoringService(
        IArchiveEntryStore entryStore,
        IArchiveAnchorStore anchorStore,
        IArchiveStore store,
        ITimestampAnchor anchor,
        ITenantContext tenantContext)
    {
        _entryStore = entryStore;
        _anchorStore = anchorStore;
        _store = store;
        _anchor = anchor;
        _tenantContext = tenantContext;
    }

    public async Task<AnchoringOutcome> AnchorChainHeadAsync(CancellationToken cancellationToken = default)
    {
        string tenant = RequireTenant();

        IReadOnlyList<ArchiveEntryRecord> chain = await _entryStore.GetChainAsync(cancellationToken);
        if (chain.Count == 0)
        {
            return new AnchoringOutcome(
                AnchoringStatus.NothingToAnchor,
                "Coffre vide : aucune tête de chaîne à ancrer.",
                Record: null);
        }

        ArchiveEntryRecord head = chain[^1];
        TimestampAnchorMethod method = _anchor.Capabilities.Method;

        if (method == TimestampAnchorMethod.None)
        {
            return new AnchoringOutcome(
                AnchoringStatus.NotAnchoredByConfiguration,
                "Aucun ancrage temporel configuré (NoAnchor) : l'intégrité repose sur la chaîne de hashes.",
                Record: null);
        }

        if (!_anchor.Capabilities.IsOperational)
        {
            // Ancrage présent mais non opérationnel en V1 (ex. OpenTimestamps, ADR-0010) : on NE l'appelle
            // pas (il lèverait) ; on le signale au lieu d'un no-op silencieux (pas de faux vert).
            return new AnchoringOutcome(
                AnchoringStatus.NotAnchoredByConfiguration,
                $"Ancrage {method} non opérationnel en V1 (voir ADR-0010) : aucune preuve produite.",
                Record: null);
        }

        // Idempotence : ne pas réancrer une tête déjà ancrée par cette méthode (un appel TSA quotidien
        // inutile si rien n'a été archivé depuis le dernier ancrage).
        ArchiveAnchorRecord? existing = await _anchorStore.GetLatestForHeadAsync(head.ChainHash, method, cancellationToken);
        if (existing is not null)
        {
            return new AnchoringOutcome(
                AnchoringStatus.AlreadyAnchored,
                $"Tête de chaîne déjà ancrée ({method}).",
                existing);
        }

        byte[] digest = DecodeHead(head.ChainHash);
        TimestampAnchorResult result = await _anchor.AnchorAsync(digest, cancellationToken);
        if (!result.IsAnchored || result.Proof is null)
        {
            return new AnchoringOutcome(AnchoringStatus.NotAnchoredByConfiguration, result.Detail, Record: null);
        }

        string headPrefix = head.ChainHash[..16];
        string proofHashPrefix = Sha256Hex.OfBytes(result.Proof)[..16];
        string extension = method == TimestampAnchorMethod.Rfc3161 ? "tsr" : "ots";
        string proofPath = ArchiveAnchorLayout.ProofPath(headPrefix, proofHashPrefix, extension);
        string manifestPath = ArchiveAnchorLayout.ProofManifestPath(headPrefix, proofHashPrefix);

        // Coffre WORM : preuve write-once PUIS son manifest (idempotents par chemin), AVANT l'indexation.
        await _store.WriteAsync(tenant, proofPath, result.Proof, cancellationToken);
        byte[] manifest = BuildAnchorManifest(head, method, result, proofPath, proofHashPrefix);
        await _store.WriteAsync(tenant, manifestPath, manifest, cancellationToken);

        ArchiveAnchorRecord record = await _anchorStore.AppendAsync(
            head.EntryId,
            head.ChainHash,
            method,
            ArchiveAnchorStatus.Anchored,
            proofPath,
            result.AnchoredUtc,
            cancellationToken);

        return new AnchoringOutcome(AnchoringStatus.Anchored, $"Tête de chaîne ancrée ({method}).", record);
    }

    private static byte[] DecodeHead(string chainHash)
    {
        try
        {
            return Convert.FromHexString(chainHash);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Le chain_hash de tête « {chainHash} » n'est pas une empreinte hexadécimale valide : ancrage impossible.", ex);
        }
    }

    private static byte[] BuildAnchorManifest(
        ArchiveEntryRecord head,
        TimestampAnchorMethod method,
        TimestampAnchorResult result,
        string proofPath,
        string proofHashPrefix)
    {
        var manifest = new JsonObject
        {
            ["schemaVersion"] = "1",
            ["entryKind"] = "anchor",
            ["method"] = method.ToString(),
            ["chainHeadEntryId"] = head.EntryId.ToString(),
            ["chainHeadHash"] = head.ChainHash,
            ["anchoredUtc"] = result.AnchoredUtc?.ToString("O", CultureInfo.InvariantCulture),
            ["proofFile"] = proofPath,
            ["proofContentType"] = result.ProofContentType,
            ["proofSha256"] = Sha256Hex.OfBytes(result.Proof!),
            ["proofHashPrefix"] = proofHashPrefix,
            ["notice"] = AnchorNotice,
        };

        return Encoding.UTF8.GetBytes(manifest.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private string RequireTenant()
    {
        if (!_tenantContext.IsResolved || string.IsNullOrWhiteSpace(_tenantContext.TenantId))
        {
            throw new InvalidOperationException(
                "L'ancrage du coffre est tenant-scopé : aucun tenant résolu pour cette opération (blueprint §7).");
        }

        return ArchivePackageLayout.SanitizeSegment(_tenantContext.TenantId);
    }
}
