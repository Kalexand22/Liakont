namespace Liakont.Modules.Supervision.Infrastructure;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Application;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Jobs;

/// <summary>
/// Évaluation de supervision pour UN tenant (item SUP01a), exécutée par <c>TenantJobRunner</c> (SOL06) une
/// fois par tenant actif. <paramref name="context"/>.Services est routé vers la base du tenant : on résout
/// le moteur scoped et on évalue toutes les règles. Le module ne fait JAMAIS sa propre boucle multi-tenant
/// (module-rules §6) — le fan-out est la seule responsabilité du runner. Si une règle a échoué pendant le
/// cycle, on lève : le runner ISOLE l'échec sur ce tenant (les autres continuent) et le remonte dans son
/// bilan, où le handler de fan-out le journalise — jamais un échec de règle silencieux.
/// </summary>
public sealed class SupervisionEvaluationTenantJob : ITenantJob
{
    public string Name => "sup.evaluation";

    public async Task ExecuteAsync(TenantJobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var engine = context.Services.GetRequiredService<IAlertEvaluationService>();
        var result = await engine.EvaluateAsync(context.TenantId, cancellationToken).ConfigureAwait(false);

        if (result.HasFailures)
        {
            var details = string.Join(" ; ", result.Failures.Select(f => $"{f.RuleKey}: {f.ErrorMessage}"));
            throw new InvalidOperationException(
                $"Évaluation de supervision incomplète pour le tenant {context.TenantId} — {result.Failures.Count} règle(s) en échec : {details}");
        }
    }
}
