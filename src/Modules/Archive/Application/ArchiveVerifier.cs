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
/// tenant et sa preuve doit être valide. Une preuve d'une méthode que l'instance ne sait pas vérifier est
/// marquée NON VÉRIFIABLE (jamais « invalide ») : confondre les deux serait un faux négatif alarmant.
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
        bool anyInvalid = false;
        bool anyNotVerifiable = false;
        bool anyUnauthenticated = false;
        bool currentHeadAnchored = false;

        foreach (ArchiveAnchorRecord anchor in anchors)
        {
            ArchiveAnchorVerification verification = await VerifyAnchorAsync(tenant, anchor, knownHeads, cancellationToken);
            anchorVerifications.Add(verification);

            switch (verification.Status)
            {
                case ArchiveAnchorVerificationStatus.Invalid:
                    anyInvalid = true;
                    break;
                case ArchiveAnchorVerificationStatus.NotVerifiable:
                    anyNotVerifiable = true;
                    break;
                case ArchiveAnchorVerificationStatus.ValidUnauthenticated:
                    anyUnauthenticated = true;
                    break;
                case ArchiveAnchorVerificationStatus.Valid when string.Equals(anchor.ChainHeadHash, currentHead, StringComparison.Ordinal):
                    // Seule une preuve AUTHENTIFIÉE (TSA épinglée) allume « coffre ancré ».
                    currentHeadAnchored = true;
                    break;
            }
        }

        bool isFullyVerified = chain.IsIntact && !anyInvalid;
        string summary = BuildSummary(chain, anchors.Count, anyInvalid, anyNotVerifiable, anyUnauthenticated, currentHeadAnchored, currentHead is not null);

        return new ArchiveVerificationReport(chain, anchorVerifications, currentHeadAnchored, isFullyVerified, summary);
    }

    private static ArchiveAnchorVerification Result(
        ArchiveAnchorRecord anchor,
        string method,
        ArchiveAnchorVerificationStatus status,
        DateTimeOffset? anchoredUtc,
        string? detail) =>
        new(anchor.AnchorId, method, anchor.ChainHeadHash, anchor.ProofPath, status, anchoredUtc, detail);

    private static string BuildSummary(
        ArchiveIntegrityReport chain,
        int anchorCount,
        bool anyInvalid,
        bool anyNotVerifiable,
        bool anyUnauthenticated,
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

        if (anchorCount == 0)
        {
            return $"{chainPart} Aucun ancrage temporel (intégrité portée par la chaîne de hashes).";
        }

        if (anyInvalid)
        {
            return $"{chainPart} AU MOINS UNE PREUVE D'ANCRAGE EST INVALIDE.";
        }

        string headPart = currentHeadAnchored
            ? "la tête de chaîne actuelle est ancrée (TSA authentifiée)."
            : "la tête de chaîne actuelle n'est pas ancrée par une TSA authentifiée.";
        string unauthenticatedPart = anyUnauthenticated
            ? " Des preuves sont cohérentes mais leur TSA n'est pas épinglée (identité non garantie ; vérification autoritaire externe)."
            : string.Empty;
        string notVerifiablePart = anyNotVerifiable
            ? " Certaines preuves relèvent d'une méthode non configurée (conservées, non vérifiées par cette instance)."
            : string.Empty;

        return $"{chainPart} {anchorCount} ancrage(s) examiné(s) ; {headPart}{unauthenticatedPart}{notVerifiablePart}";
    }

    private async Task<ArchiveAnchorVerification> VerifyAnchorAsync(
        string tenant,
        ArchiveAnchorRecord anchor,
        HashSet<string> knownHeads,
        CancellationToken cancellationToken)
    {
        string method = anchor.Method.ToString();

        ArchiveAnchorVerification Fail(ArchiveAnchorVerificationStatus status, string detail) =>
            Result(anchor, method, status, anchor.AnchoredUtc, detail);

        // 1. La tête ancrée doit être une tête réelle de la chaîne du tenant (sinon : preuve orpheline).
        if (!knownHeads.Contains(anchor.ChainHeadHash))
        {
            return Fail(
                ArchiveAnchorVerificationStatus.Invalid,
                "La tête de chaîne ancrée est inconnue de la chaîne du tenant (preuve orpheline ou chaîne altérée).");
        }

        if (string.IsNullOrEmpty(anchor.ProofPath))
        {
            return Fail(ArchiveAnchorVerificationStatus.Invalid, "Ancrage sans preuve dans le coffre : rien à vérifier.");
        }

        // 2. Une preuve d'une autre méthode que l'ancrage configuré n'est pas une altération : NON vérifiable.
        if (_anchor.Capabilities.Method != anchor.Method)
        {
            return Fail(
                ArchiveAnchorVerificationStatus.NotVerifiable,
                $"Preuve produite par une méthode ({method}) différente de l'ancrage configuré ({_anchor.Capabilities.Method}) : non vérifiable par cette instance.");
        }

        byte[] proof;
        try
        {
            proof = await _store.ReadAsync(tenant, anchor.ProofPath, cancellationToken);
        }
        catch (ArchiveObjectNotFoundException)
        {
            return Fail(
                ArchiveAnchorVerificationStatus.Invalid,
                $"Preuve d'ancrage manquante dans le coffre ({anchor.ProofPath}).");
        }

        byte[] digest;
        try
        {
            digest = Convert.FromHexString(anchor.ChainHeadHash);
        }
        catch (FormatException)
        {
            return Fail(ArchiveAnchorVerificationStatus.Invalid, "Empreinte de tête de chaîne non hexadécimale.");
        }

        TimestampVerification verification = await _anchor.VerifyAsync(proof, digest, cancellationToken);

        ArchiveAnchorVerificationStatus verifiedStatus;
        if (!verification.IsValid)
        {
            verifiedStatus = ArchiveAnchorVerificationStatus.Invalid;
        }
        else if (verification.IsAuthorityAuthenticated)
        {
            verifiedStatus = ArchiveAnchorVerificationStatus.Valid;
        }
        else
        {
            verifiedStatus = ArchiveAnchorVerificationStatus.ValidUnauthenticated;
        }

        // On remonte le détail même quand valide : il porte le caveat d'authentification de la TSA.
        return Result(anchor, method, verifiedStatus, verification.AnchoredUtc ?? anchor.AnchoredUtc, verification.Detail);
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
