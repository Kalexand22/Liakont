namespace Liakont.Host.Supervision;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Témoin de vie de la supervision (FIX210, F12 §5.1). L'évaluation du dead-man's-switch est un job SYSTÈME
/// (fan-out cross-tenant) : sa dernière exécution vit dans la base SYSTÈME. On la lit via le Contract du module
/// Supervision (<see cref="ISupervisionLivenessQueries"/>) résolu dans un scope SYSTÈME — un scope DI neuf sans
/// tenant ambiant route la connexion vers la base système (même mécanique que l'amorçage au démarrage). Le
/// type technique du job reste interne au module Supervision (le Host ne dépend que des Contracts). Au-delà du
/// double de la cadence (F12 §5.1), le dispositif est jugé en retard. Lecture best-effort : un échec rend un
/// état « indéterminé », jamais une fausse alerte.
/// </summary>
internal sealed partial class SupervisionLivenessProvider : ISupervisionLivenessProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SupervisionLivenessProvider> _logger;

    public SupervisionLivenessProvider(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<SupervisionLivenessProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SupervisionLivenessView> GetAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset? lastEvaluationUtc;
        try
        {
            // Scope DI neuf SANS tenant ambiant ⇒ la connexion route vers la base SYSTÈME (où vivent les jobs
            // système). Lecture par le Contract du module Supervision (jamais de SQL maison cross-module, CLAUDE.md n°14).
            await using var scope = _scopeFactory.CreateAsyncScope();
            var livenessQueries = scope.ServiceProvider.GetRequiredService<ISupervisionLivenessQueries>();
            lastEvaluationUtc = await livenessQueries.GetLastEvaluationUtcAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort : un échec de lecture ne doit jamais bloquer la page ni afficher une fausse alerte.
            LogLivenessReadFailed(_logger, ex);
            return new SupervisionLivenessView
            {
                LastEvaluationUtc = null,
                Status = SupervisionLivenessStatus.Unknown,
                IntervalMinutes = SupervisionEvaluationCadence.IntervalMinutes,
            };
        }

        return Evaluate(lastEvaluationUtc, _timeProvider.GetUtcNow(), SupervisionEvaluationCadence.IntervalMinutes);
    }

    /// <summary>
    /// Décision PURE (testable) du témoin de vie : jamais évaluée (pas d'exécution), en retard (au-delà du
    /// double de la cadence — au moins une évaluation manquée plus une marge) ou saine.
    /// </summary>
    internal static SupervisionLivenessView Evaluate(DateTimeOffset? lastEvaluationUtc, DateTimeOffset now, int intervalMinutes)
    {
        if (lastEvaluationUtc is not { } when)
        {
            return new SupervisionLivenessView
            {
                LastEvaluationUtc = null,
                Status = SupervisionLivenessStatus.NeverEvaluated,
                IntervalMinutes = intervalMinutes,
            };
        }

        var overdueAfter = TimeSpan.FromMinutes(intervalMinutes * 2L);
        var status = now - when > overdueAfter
            ? SupervisionLivenessStatus.Overdue
            : SupervisionLivenessStatus.Healthy;

        return new SupervisionLivenessView
        {
            LastEvaluationUtc = when,
            Status = status,
            IntervalMinutes = intervalMinutes,
        };
    }

    [LoggerMessage(
        EventId = 7230,
        Level = LogLevel.Warning,
        Message = "Lecture du témoin de vie de la supervision impossible — bandeau « état indéterminé » affiché.")]
    private static partial void LogLivenessReadFailed(ILogger logger, Exception exception);
}
