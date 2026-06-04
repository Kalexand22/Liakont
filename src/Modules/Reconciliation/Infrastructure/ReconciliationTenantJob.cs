namespace Liakont.Modules.Reconciliation.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Reconciliation.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Travail de réconciliation POUR UN SEUL TENANT (item TRK07, F06 §7 §6), exécuté par le
/// <c>TenantJobRunner</c> (SOL06) — la mécanique unique de balayage multi-tenant (aucune boucle
/// « pour chaque tenant » maison, module-rules §6). Le contexte fournit un <see cref="IServiceProvider"/>
/// déjà routé vers la base du tenant : on y résout <see cref="IReconciliationService"/> (tenant-scopé) et
/// on lance une passe de réconciliation.
/// </summary>
internal sealed class ReconciliationTenantJob : ITenantJob
{
    public string Name => "trk.reconciliation";

    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        IReconciliationService service = context.Services.GetRequiredService<IReconciliationService>();
        await service.RunForCurrentTenantAsync(cancellationToken);
    }
}
