namespace Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;

using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Travail de bascule tacite des acceptations 389 pour UN tenant (MND04, ADR-0024 §4), exécuté par
/// <c>TenantJobRunner</c> (SOL06) une fois par tenant actif. <paramref name="context"/>.Services est routé
/// vers la base du tenant : on résout le service scoped habituel et on traite les acceptations dues du
/// tenant. Le module ne fait JAMAIS sa propre boucle multi-tenant (module-rules §6, INV-ACCEPT-6) — le
/// fan-out est la seule responsabilité du runner.
/// </summary>
public sealed class SelfBilledAcceptanceTacitJob : ITenantJob
{
    public string Name => "mandats.self-billed-tacit-acceptance";

    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var service = context.Services.GetRequiredService<ITacitAcceptanceService>();
        await service.ProcessDueAsync(cancellationToken);
    }
}
