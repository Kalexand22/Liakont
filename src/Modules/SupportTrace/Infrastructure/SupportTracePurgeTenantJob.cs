namespace Liakont.Modules.SupportTrace.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.SupportTrace.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Purge de la trace de support pour UN tenant (FX06), exécutée par <c>TenantJobRunner</c> (SOL06) une fois
/// par tenant actif. <paramref name="context"/>.Services est routé vers le scope du tenant ; on résout le
/// service de purge et on applique la rétention configurée. Le module ne fait JAMAIS sa propre boucle
/// multi-tenant (module-rules §6) — le fan-out est la seule responsabilité du runner.
/// </summary>
public sealed class SupportTracePurgeTenantJob : ITenantJob
{
    public string Name => "support-trace.purge";

    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var purge = context.Services.GetRequiredService<ISupportTracePurgeService>();
        await purge.PurgeExpiredAsync(context.TenantId, cancellationToken);
    }
}
