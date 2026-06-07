namespace Liakont.Host.Staging;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Domain;
using Liakont.Modules.Staging.Contracts;
using Stratum.Common.Abstractions.MultiTenancy;

/// <summary>
/// Adaptateur de composition root (Host) : implémente le port <see cref="IArchivedDocumentProbe"/> du module
/// Staging en sondant le coffre d'archive concret (<see cref="IArchiveStore"/>). Il vit dans le Host —
/// PAS dans le module Staging — parce que le Host est le SEUL endroit autorisé à référencer un autre module
/// hors de ses Contracts (blueprint §6 ; CLAUDE.md n°14) : le câblage cross-module est précisément le rôle
/// du composition root. Aucune logique métier ici (pas de TVA/validation/état) — uniquement de la glue qui
/// dérive le chemin du paquet (via <see cref="ArchivePackageLayout"/>, l'autorité du coffre sur sa propre
/// disposition) et interroge la présence EFFECTIVE du blob WORM (ADR-0014 §4).
/// </summary>
internal sealed class ArchiveStoreArchivedDocumentProbe : IArchivedDocumentProbe
{
    private readonly IArchiveStore _archiveStore;
    private readonly ITenantContext _tenantContext;

    public ArchiveStoreArchivedDocumentProbe(IArchiveStore archiveStore, ITenantContext tenantContext)
    {
        _archiveStore = archiveStore ?? throw new ArgumentNullException(nameof(archiveStore));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public Task<bool> IsArchivedAsync(ArchivedDocumentLocator locator, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locator);

        if (!_tenantContext.IsResolved || string.IsNullOrWhiteSpace(_tenantContext.TenantId))
        {
            throw new InvalidOperationException(
                "La sonde de présence WORM est tenant-scopée : aucun tenant résolu pour cette opération (blueprint §7).");
        }

        // Même disposition que ArchiveService : le manifest est l'objet scellé de référence du paquet.
        string packageDirectory = ArchivePackageLayout.PackageDirectory(
            locator.IssueYear, locator.IssueMonth, locator.DocumentNumber);
        string manifestPath = ArchivePackageLayout.Combine(packageDirectory, ArchivePackageLayout.ManifestFileName);

        // Le store ré-assainit le segment de tenant ; on lui passe le tenant courant tel quel.
        return _archiveStore.ExistsAsync(_tenantContext.TenantId, manifestPath, cancellationToken);
    }
}
