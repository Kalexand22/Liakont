namespace Liakont.Modules.Signature.Infrastructure.Drain;

using Liakont.Modules.Signature.Application;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Travail de drain des webhooks de signature pour UN tenant (ADR-0029 §5), exécuté par
/// <c>TenantJobRunner</c> (SOL06) une fois par tenant actif. <c>context.Services</c> est routé vers la base du
/// tenant : on résout le service de drain scopé et on traite l'inbox du tenant. Le module ne fait JAMAIS sa
/// propre boucle multi-tenant (module-rules §6) — le fan-out est la seule responsabilité du runner.
/// </summary>
public sealed class SignatureWebhookDrainJob : ITenantJob
{
    public string Name => "signature.webhook-drain";

    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var service = context.Services.GetRequiredService<ISignatureWebhookDrainService>();
        await service.DrainAsync(cancellationToken);
    }
}
