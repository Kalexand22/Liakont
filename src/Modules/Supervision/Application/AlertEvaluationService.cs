namespace Liakont.Modules.Supervision.Application;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Domain;

/// <summary>
/// Implémentation du moteur de supervision (SUP01a). Pour chaque règle enregistrée :
/// <list type="bullet">
///   <item>condition remplie + aucune alerte active → DÉCLENCHE une alerte (anti-bruit : une seule active par règle) ;</item>
///   <item>condition remplie + alerte déjà active → RIEN (pas de re-déclenchement) ;</item>
///   <item>condition non remplie + alerte active → AUTO-RÉSOLUTION ;</item>
///   <item>condition non remplie + aucune alerte → RIEN.</item>
/// </list>
/// Une règle qui lève est ISOLÉE (consignée dans le bilan, les autres règles continuent). L'horloge est
/// injectée (<see cref="TimeProvider"/>) avec un défaut sûr <see cref="TimeProvider.System"/> — même motif
/// que les autres services horodatés du produit (tests déterministes via un <c>TimeProvider</c> figé).
/// </summary>
public sealed class AlertEvaluationService : IAlertEvaluationService
{
    private readonly IEnumerable<IAlertRule> _rules;
    private readonly IAlertStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly IAlertNotifier _notifier;

    public AlertEvaluationService(IEnumerable<IAlertRule> rules, IAlertStore store)
        : this(rules, store, TimeProvider.System, NullAlertNotifier.Instance)
    {
    }

    public AlertEvaluationService(IEnumerable<IAlertRule> rules, IAlertStore store, TimeProvider timeProvider)
        : this(rules, store, timeProvider, NullAlertNotifier.Instance)
    {
    }

    public AlertEvaluationService(IEnumerable<IAlertRule> rules, IAlertStore store, TimeProvider timeProvider, IAlertNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(notifier);

        _rules = rules;
        _store = store;
        _timeProvider = timeProvider;
        _notifier = notifier;
    }

    public async Task<AlertEvaluationResult> EvaluateAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var nowUtc = _timeProvider.GetUtcNow();
        var context = new AlertEvaluationContext(tenantId, nowUtc);
        var failures = new List<RuleEvaluationFailure>();
        var evaluated = 0;

        foreach (var rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var evaluation = await rule.EvaluateAsync(context, cancellationToken).ConfigureAwait(false);
                await ApplyAsync(tenantId, rule, evaluation, nowUtc, cancellationToken).ConfigureAwait(false);
                evaluated++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // L'annulation demandée par l'appelant interrompt tout le cycle (jamais avalée comme un échec de règle).
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new RuleEvaluationFailure(rule.RuleKey, ex.Message));
            }
        }

        return new AlertEvaluationResult(evaluated, failures);
    }

    private async Task ApplyAsync(
        string tenantId,
        IAlertRule rule,
        AlertEvaluation evaluation,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var active = await _store.FindActiveByRuleAsync(rule.RuleKey, cancellationToken).ConfigureAwait(false);

        if (evaluation.IsFiring)
        {
            if (active is null)
            {
                // Anti-bruit : on ne crée une alerte que s'il n'y en a pas déjà une active pour cette règle.
                var alert = Alert.Raise(tenantId, rule.RuleKey, rule.Severity, evaluation.Detail, nowUtc);
                await _store.InsertAsync(alert, cancellationToken).ConfigureAwait(false);

                // Notification au DÉCLENCHEMENT uniquement (jamais sur « déjà active ») → anti-spam par
                // construction. Le notifieur ne lève jamais (fire-and-log) : une notification ne casse pas
                // l'évaluation des règles (SUP03 §4).
                await _notifier.NotifyRaisedAsync(alert, cancellationToken).ConfigureAwait(false);
            }

            // active != null : alerte déjà active → pas de re-déclenchement (anti-bruit).
            return;
        }

        if (active is not null)
        {
            // Auto-résolution : la condition a disparu, on clôt l'alerte active.
            active.Resolve(nowUtc);
            await _store.ResolveAsync(active, cancellationToken).ConfigureAwait(false);

            // Notification de résolution (optionnelle côté implémentation) — à la transition uniquement.
            await _notifier.NotifyResolvedAsync(active, cancellationToken).ConfigureAwait(false);
        }
    }
}
