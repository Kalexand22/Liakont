namespace Liakont.Modules.Archive.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Archive.Application;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Travail d'ancrage pour UN tenant (TRK06), exécuté par <c>TenantJobRunner</c> (SOL06) une fois par
/// tenant actif. <paramref name="context"/>.Services est routé vers la base du tenant : on résout le
/// service d'ancrage scoped habituel et on ancre la tête de chaîne. Le module ne fait JAMAIS sa propre
/// boucle multi-tenant (module-rules §6) — le fan-out est la seule responsabilité du runner.
/// </summary>
public sealed class DailyAnchoringTenantJob : ITenantJob
{
    public string Name => "archive.daily-anchoring";

    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var anchoring = context.Services.GetRequiredService<IArchiveAnchoringService>();
        await anchoring.AnchorChainHeadAsync(cancellationToken);
    }
}
