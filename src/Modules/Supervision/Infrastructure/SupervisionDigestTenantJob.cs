namespace Liakont.Modules.Supervision.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Récapitulatif quotidien (digest) des alertes actives pour UN tenant (SUP03 §3, OPTIONNEL), exécuté par
/// <c>TenantJobRunner</c> (SOL06) une fois par tenant actif. <paramref name="context"/>.Services est routé
/// vers la base du tenant : on résout le <see cref="IAlertDigestSender"/> scoped et on lui demande d'envoyer
/// le digest des alertes ACTIVES du tenant courant à l'opérateur. Le sender est gardé en interne par
/// <c>SupervisionNotificationOptions.DailyDigestEnabled</c> (no-op si désactivé ou aucune alerte active) et
/// ne lève jamais — un échec de digest n'est pas une panne de supervision. Le module ne fait JAMAIS sa
/// propre boucle multi-tenant (module-rules §6) : le fan-out est la seule responsabilité du runner.
/// </summary>
public sealed class SupervisionDigestTenantJob : ITenantJob
{
    public string Name => "sup.digest";

    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sender = context.Services.GetRequiredService<IAlertDigestSender>();
        await sender.SendActiveAlertsDigestAsync(context.TenantId, cancellationToken).ConfigureAwait(false);
    }
}
