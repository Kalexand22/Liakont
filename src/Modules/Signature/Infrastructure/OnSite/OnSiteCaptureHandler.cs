namespace Liakont.Modules.Signature.Infrastructure.OnSite;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Signature.Application.OnSite;
using Liakont.Modules.Signature.Contracts.OnSite;
using Liakont.Modules.SupportTrace.Contracts;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Proxy <c>OnSiteCapture</c> (ADR-0030 §3/§4/§5 ; F17 §6) : traite la capture postée par le client soft
/// Wacom. Aucune logique métier ne vit côté client — TOUTE la décision est ici, côté plateforme. Étapes :
/// <list type="number">
///   <item>Tenant-scoping serveur (CLAUDE.md n°9) : re-vérifie <c>document_id → company_id</c> via
///   <see cref="IDocumentQueries"/> (database-per-tenant) → <c>NotFound</c> sinon (jamais de confiance dans
///   un <c>company_id</c> envoyé par le client).</item>
///   <item>Relit les OCTETS EXACTS de l'artefact Factur-X scellé (<see cref="ISupportTraceStore"/>) et vérifie
///   le BINDING : <c>re-hash == hash signé</c> (SHA-256, même flux — ADR-0030 §4, INV-ONSITE-6).</item>
///   <item>Résout le SIGNATAIRE vérifié via la liaison séparée (<see cref="IOnSiteSignerBindingStore"/>),
///   JAMAIS depuis le payload ni le déposant (ADR-0030 §5, INV-ONSITE-7).</item>
///   <item>Rapatrie la preuve (PNG) en WORM via <see cref="IArchiveService"/> (Archive.Contracts, jamais un
///   backend concret) puis consigne la preuve dans le journal append-only.</item>
/// </list>
/// Le niveau reste SES (ADR-0030 §6) ; AUCUN gabarit biométrique n'est dérivé de la FSS (ADR-0030 §8).
/// </summary>
internal sealed class OnSiteCaptureHandler : IRequestHandler<OnSiteCaptureCommand, OnSiteCaptureResult>
{
    private const string OnSiteLevel = "SES";
    private const string AddendumKind = "onsite-signature";

    private readonly IDocumentQueries _documents;
    private readonly ISupportTraceStore _sealedArtifacts;
    private readonly IOnSiteSignerBindingStore _signerBindings;
    private readonly IArchiveService _archive;
    private readonly IOnSiteSignatureProofStore _proofs;
    private readonly ITenantContext _tenantContext;

    public OnSiteCaptureHandler(
        IDocumentQueries documents,
        ISupportTraceStore sealedArtifacts,
        IOnSiteSignerBindingStore signerBindings,
        IArchiveService archive,
        IOnSiteSignatureProofStore proofs,
        ITenantContext tenantContext)
    {
        _documents = documents;
        _sealedArtifacts = sealedArtifacts;
        _signerBindings = signerBindings;
        _archive = archive;
        _proofs = proofs;
        _tenantContext = tenantContext;
    }

    public async Task<OnSiteCaptureResult> Handle(OnSiteCaptureCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Tenant-scoping serveur (CLAUDE.md n°9). La base EST le tenant : un document d'un AUTRE tenant est
        //    introuvable → 404. Aucune confiance dans un company_id porté par le client.
        var document = await _documents.GetByIdAsync(request.DocumentId, cancellationToken)
            ?? throw new NotFoundException("Document", request.DocumentId);

        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException(
                "Aucun tenant résolu pour la capture sur place : l'endpoint OnSiteCapture exige une authentification tenant-scopée.");

        // 2. Octets EXACTS de l'artefact Factur-X scellé transmis (ADR-0030 §4). Absent (jamais émis en
        //    Factur-X, ou trace purgée) → binding non vérifiable : capture refusée sans preuve.
        var sealedArtifact = await _sealedArtifacts.ReadAsync(tenantId, request.DocumentId, cancellationToken);
        if (sealedArtifact is null)
        {
            return new OnSiteCaptureResult
            {
                BindingVerified = false,
                SignerIdentityVerified = false,
                Level = OnSiteLevel,
                Message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"L'artefact Factur-X scellé du document {document.DocumentNumber} est introuvable : la signature sur place ne peut pas être liée. Vérifiez que le document a bien été émis en Factur-X."),
            };
        }

        // 3. Binding : re-hash des octets exacts == empreinte signée par le client (à temps constant).
        if (!OnSiteBindingHasher.Verify(sealedArtifact, request.SignedBindingHash))
        {
            return new OnSiteCaptureResult
            {
                BindingVerified = false,
                SignerIdentityVerified = false,
                Level = OnSiteLevel,
                Message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"L'empreinte de signature ne correspond pas à l'artefact scellé du document {document.DocumentNumber} : la capture est refusée (le binding doit porter sur les octets exacts du Factur-X)."),
            };
        }

        // 4. Signataire VÉRIFIÉ résolu par la liaison séparée — JAMAIS le payload ni le déposant (INV-ONSITE-7).
        var verifiedBinding = await _signerBindings.ResolveVerifiedAsync(
            request.CompanyId, request.DocumentId, cancellationToken);
        var signerVerified = verifiedBinding is not null;

        // 5. Rapatriement WORM de la preuve (PNG) via Archive.Contracts — jamais un backend concret (CLAUDE.md n°6).
        var proofPng = Convert.FromBase64String(request.SignatureImagePngBase64);
        var archived = await _archive.AddAddendumAsync(
            new ArchiveAddendumRequest
            {
                DocumentId = document.Id,
                DocumentNumber = document.DocumentNumber,
                IssueDate = document.IssueDate,
                Kind = AddendumKind,
                Attachment = new ArchiveAttachment("onsite-signature.png", "image/png", proofPng),
            },
            cancellationToken);

        // 6. Journal append-only de la preuve (métadonnée seule ; aucun gabarit biométrique — INV-ONSITE-10).
        var binding = OnSiteBindingHasher.ComputeHex(sealedArtifact);
        var proof = new OnSiteSignatureProofRecord
        {
            Id = Guid.NewGuid(),
            CompanyId = request.CompanyId,
            DocumentId = request.DocumentId,
            BindingHash = binding,
            UploaderUserId = request.UploaderUserId,
            SignerIdentity = verifiedBinding?.SignerIdentity,
            SignerVerified = signerVerified,
            Level = OnSiteLevel,
            ProofArchiveRef = archived.PackageHash,
            CapturedAtUtc = request.CapturedAtUtc,
        };
        await _proofs.AppendAsync(proof, cancellationToken);

        var signerNote = signerVerified
            ? "signataire vérifié (liaison en personne par la SVV)"
            : "signataire non encore vérifié (la preuve reste de niveau SES)";
        return new OnSiteCaptureResult
        {
            ProofId = proof.Id,
            BindingVerified = true,
            SignerIdentityVerified = signerVerified,
            Level = OnSiteLevel,
            Message = string.Create(
                CultureInfo.InvariantCulture,
                $"Signature sur place enregistrée pour le document {document.DocumentNumber} ({signerNote}) ; preuve rapatriée dans le coffre d'archive."),
        };
    }
}
