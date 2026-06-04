namespace Liakont.Modules.Archive.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Vérifieur d'intégrité COMPLET du coffre d'un tenant (TRK06). Réutilise la vérification contenu +
/// chaînage de TRK05 (<see cref="IArchiveService.VerifyTenantChainAsync"/>) et l'enrichit de la
/// vérification des preuves d'ancrage temporel : chaque ancrage doit pointer une tête de chaîne RÉELLE du
/// tenant et sa preuve doit être cryptographiquement valide. Tenant-scopé par construction.
/// </summary>
public sealed class ArchiveVerifier : IArchiveVerifier
{
    private readonly IArchiveService _archiveService;
    private readonly IArchiveEntryStore _entryStore;
    private readonly IArchiveAnchorStore _anchorStore;
    private readonly IArchiveStore _store;
    private readonly ITimestampAnchor _anchor;
    private readonly ITenantContext _tenantContext;

    public ArchiveVerifier(
        IArchiveService archiveService,
        IArchiveEntryStore entryStore,
        IArchiveAnchorStore anchorStore,
        IArchiveStore store,
        ITimestampAnchor anchor,
        ITenantContext tenantContext)
    {
        _archiveService = archiveService;
        _entryStore = entryStore;
        _anchorStore = anchorStore;
        _store = store;
        _anchor = anchor;
        _tenantContext = tenantContext;
    }

    public async Task<ArchiveVerificationReport> VerifyTenantVaultAsync(CancellationToken cancellationToken = default)
    {
        string tenant = RequireTenant();

        ArchiveIntegrityReport chain = await _archiveService.VerifyTenantChainAsync(cancellationToken);
        IReadOnlyList<ArchiveEntryRecord> entries = await _entryStore.GetChainAsync(cancellationToken);
        IReadOnlyList<ArchiveAnchorRecord> anchors = await _anchorStore.GetAnchorsAsync(cancellationToken);

        var knownHeads = new HashSet<string>(StringComparer.Ordinal);
        foreach (ArchiveEntryRecord entry in entries)
        {
            knownHeads.Add(entry.ChainHash);
        }

        string? currentHead = entries.Count > 0 ? entries[^1].ChainHash : null;

        var anchorVerifications = new List<ArchiveAnchorVerification>(anchors.Count);
        bool allAnchorsValid = true;
        bool currentHeadAnchored = false;

        foreach (ArchiveAnchorRecord anchor in anchors)
        {
            ArchiveAnchorVerification verification = await VerifyAnchorAsync(tenant, anchor, knownHeads, cancellationToken);
            anchorVerifications.Add(verification);

            if (!verification.IsValid)
            {
                allAnchorsValid = false;
            }
            else if (string.Equals(anchor.ChainHeadHash, currentHead, StringComparison.Ordinal))
            {
                currentHeadAnchored = true;
            }
        }

        bool isFullyVerified = chain.IsIntact && allAnchorsValid;
        string summary = BuildSummary(chain, anchors.Count, allAnchorsValid, currentHeadAnchored, currentHead is not null);

        return new ArchiveVerificationReport(chain, anchorVerifications, currentHeadAnchored, isFullyVerified, summary);
    }

    private static ArchiveAnchorVerification Invalid(ArchiveAnchorRecord anchor, string method, string detail) =>
        new(anchor.AnchorId, method, anchor.ChainHeadHash, anchor.ProofPath, IsValid: false, anchor.AnchoredUtc, detail);

    private static string BuildSummary(
        ArchiveIntegrityReport chain,
        int anchorCount,
        bool allAnchorsValid,
        bool currentHeadAnchored,
        bool hasEntries)
    {
        if (!hasEntries)
        {
            return "Coffre vide : aucune entrée à vérifier.";
        }

        string chainPart = chain.IsIntact
            ? $"Chaîne intacte ({chain.EntryCount} entrée(s))."
            : $"CHAÎNE ROMPUE : {chain.FirstBreakDetail}";

        string anchorPart = anchorCount == 0
            ? "Aucun ancrage temporel (intégrité portée par la chaîne de hashes)."
            : allAnchorsValid
                ? currentHeadAnchored
                    ? $"{anchorCount} ancrage(s) valide(s) ; la tête de chaîne actuelle est ancrée."
                    : $"{anchorCount} ancrage(s) valide(s) ; la tête de chaîne actuelle n'est pas encore ancrée."
                : "AU MOINS UNE PREUVE D'ANCRAGE EST INVALIDE.";

        return $"{chainPart} {anchorPart}";
    }

    private async Task<ArchiveAnchorVerification> VerifyAnchorAsync(
        string tenant,
        ArchiveAnchorRecord anchor,
        HashSet<string> knownHeads,
        CancellationToken cancellationToken)
    {
        string method = anchor.Method.ToString();

        // 1. La tête ancrée doit être une tête réelle de la chaîne du tenant (sinon : preuve orpheline).
        if (!knownHeads.Contains(anchor.ChainHeadHash))
        {
            return Invalid(anchor, method, "La tête de chaîne ancrée est inconnue de la chaîne du tenant (preuve orpheline ou chaîne altérée).");
        }

        if (string.IsNullOrEmpty(anchor.ProofPath))
        {
            return Invalid(anchor, method, "Ancrage sans preuve dans le coffre : rien à vérifier.");
        }

        // 2. La preuve n'est vérifiable que par l'ancrage de la MÊME méthode (capacité, jamais un test de type).
        if (_anchor.Capabilities.Method != anchor.Method)
        {
            return Invalid(
                anchor,
                method,
                $"Preuve produite par une méthode ({method}) différente de l'ancrage configuré ({_anchor.Capabilities.Method}) : non vérifiable par cette instance.");
        }

        byte[] proof;
        try
        {
            proof = await _store.ReadAsync(tenant, anchor.ProofPath, cancellationToken);
        }
        catch (ArchiveObjectNotFoundException)
        {
            return Invalid(anchor, method, $"Preuve d'ancrage manquante dans le coffre ({anchor.ProofPath}).");
        }

        byte[] digest;
        try
        {
            digest = Convert.FromHexString(anchor.ChainHeadHash);
        }
        catch (FormatException)
        {
            return Invalid(anchor, method, "Empreinte de tête de chaîne non hexadécimale.");
        }

        TimestampVerification verification = await _anchor.VerifyAsync(proof, digest, cancellationToken);
        return new ArchiveAnchorVerification(
            anchor.AnchorId,
            method,
            anchor.ChainHeadHash,
            anchor.ProofPath,
            verification.IsValid,
            verification.AnchoredUtc ?? anchor.AnchoredUtc,
            verification.IsValid ? null : verification.Detail);
    }

    private string RequireTenant()
    {
        if (!_tenantContext.IsResolved || string.IsNullOrWhiteSpace(_tenantContext.TenantId))
        {
            throw new InvalidOperationException(
                "La vérification du coffre est tenant-scopée : aucun tenant résolu pour cette opération (blueprint §7).");
        }

        return ArchivePackageLayout.SanitizeSegment(_tenantContext.TenantId);
    }
}
