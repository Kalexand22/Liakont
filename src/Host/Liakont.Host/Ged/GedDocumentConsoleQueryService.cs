namespace Liakont.Host.Ged;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Host.Security;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Ged.Contracts.Consultation;
using Liakont.Modules.Ged.Contracts.Queries;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Service d'assemblage de la fiche document GED (GED09b, F19 §6.7). Compose trois surfaces de CONTRAT :
/// <list type="number">
/// <item><description><see cref="IGedDocumentQueries"/> — méta + axes + entités (masquage confidentiel
/// server-side, selon le droit <c>liakont.ged.confidential</c> résolu ici).</description></item>
/// <item><description><see cref="IManagedArchiveReader"/> — intégrité RE-LUE du coffre (vs <c>content_hash</c>)
/// et aperçu <c>ReadableHtml</c> ; appelé UNIQUEMENT pour un document GED-seul. Pour un document fiscal lié
/// (<c>archive_entry_id</c> non nul), l'intégrité de référence est la chaîne + ancrage du coffre FISCAL
/// (IArchiveVerifier, réservé au fiscal, F19 §6.7) : on la SURFACE, on ne ré-applique pas la re-lecture GED.</description></item>
/// <item><description><see cref="IConsultationAuditWriter"/> — trace <c>view_document</c> (best-effort par
/// défaut ; fail-closed en régime probant, §6.6).</description></item>
/// </list>
/// Le service ne franchit AUCUNE frontière de module : il ne parle qu'aux Contracts (Ged + Archive). Aucune
/// règle métier ni recalcul de montant (RL-22).
/// </summary>
internal sealed class GedDocumentConsoleQueryService : IGedDocumentConsoleQueries
{
    private readonly IGedDocumentQueries _documents;
    private readonly IManagedArchiveReader _archiveReader;
    private readonly IConsultationAuditWriter _consultationWriter;
    private readonly IPermissionService _permissions;

    public GedDocumentConsoleQueryService(
        IGedDocumentQueries documents,
        IManagedArchiveReader archiveReader,
        IConsultationAuditWriter consultationWriter,
        IPermissionService permissions)
    {
        _documents = documents;
        _archiveReader = archiveReader;
        _consultationWriter = consultationWriter;
        _permissions = permissions;
    }

    public async Task<GedDocumentDetailViewModel?> GetAsync(Guid managedDocumentId, CancellationToken cancellationToken = default)
    {
        // Droit confidentiel de l'acteur (§6.5) : décide le masquage server-side ET la valeur portée à la trace
        // de consultation. Défaut sûr = false (masquer en cas d'oubli).
        bool hasConfidentialRight = _permissions.HasPermission(LiakontPermissions.GedConfidential);

        GedManagedDocumentView? document = await _documents.GetAsync(managedDocumentId, hasConfidentialRight, cancellationToken);
        if (document is null)
        {
            return null;
        }

        // Journal de consultation (view_document) : best-effort par défaut (une trace ratée ne casse pas la
        // lecture) ; en régime probant, l'écriture LÈVE et la page (try/catch) traduit en refus d'accès (§6.6).
        await _consultationWriter.WriteAsync(
            new ConsultationLogEntry
            {
                Action = ConsultationAction.ViewDocument,
                ManagedDocumentId = managedDocumentId,
                ActorHasConfidentialAccess = hasConfidentialRight,
            },
            cancellationToken);

        bool fiscalLinked = document.ArchiveEntryId is not null;
        GedDocumentIntegrityView integrity;
        string? previewHtml = null;

        if (fiscalLinked)
        {
            // Intégrité fiscale (chaîne + ancrage) = IArchiveVerifier, réservé au document fiscal (F19 §6.7). On
            // SURFACE le rattachement, on ne ré-applique pas la re-lecture GED à un paquet fiscal.
            integrity = new GedDocumentIntegrityView(
                GedDocumentIntegrityState.FiscalLinked, document.ContentHash, null, null);
        }
        else
        {
            GedArchiveIntegrityResult result = await _archiveReader.VerifyManagedPackageAsync(
                document.ArchivePath, document.ContentHash, cancellationToken);
            integrity = new GedDocumentIntegrityView(
                MapIntegrity(result.Status), result.IndexedContentHash, result.RecomputedContentHash, result.Detail);

            previewHtml = await _archiveReader.ReadManagedReadableHtmlAsync(document.ArchivePath, cancellationToken);
        }

        return new GedDocumentDetailViewModel
        {
            Id = document.Id,
            Title = document.Title,
            DocKind = document.DocKind,
            Status = document.Status,
            RetentionClass = document.RetentionClass,
            DeferReason = document.DeferReason,
            Integrity = integrity,
            PreviewHtml = previewHtml,
            IsFiscalLinked = fiscalLinked,
            FiscalDocumentId = document.FiscalDocumentId,
            CreatedUtc = document.CreatedUtc,
            UpdatedUtc = document.UpdatedUtc,
            Axes = document.Axes,
            Entities = document.Entities,
        };
    }

    // Mapping EXHAUSTIF du statut d'intégrité de coffre (contrat Archive) vers l'état affiché sur la fiche. Un défaut
    // silencieux `_ => NotArchived` AVALERAIT toute valeur future de GedArchiveIntegrityStatus : un nouveau verdict
    // d'intégrité s'afficherait « pas encore rangé dans le coffre » — verdict d'intégrité TROMPEUR sur un produit de
    // conformité. On échoue donc BRUYAMMENT sur une valeur inconnue (le mapping console doit être étendu), plutôt que
    // de masquer une divergence d'intégrité (P2 GDF12).
    private static GedDocumentIntegrityState MapIntegrity(GedArchiveIntegrityStatus status) => status switch
    {
        GedArchiveIntegrityStatus.Verified => GedDocumentIntegrityState.Verified,
        GedArchiveIntegrityStatus.Altered => GedDocumentIntegrityState.Altered,
        GedArchiveIntegrityStatus.Missing => GedDocumentIntegrityState.Missing,
        GedArchiveIntegrityStatus.NotArchived => GedDocumentIntegrityState.NotArchived,
        _ => throw new ArgumentOutOfRangeException(
            nameof(status),
            status,
            "Statut d'intégrité de coffre GED inconnu : le mapping de la fiche console doit être étendu (aucun défaut silencieux ne doit masquer un nouveau verdict d'intégrité)."),
    };
}
