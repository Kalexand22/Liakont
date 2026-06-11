namespace Liakont.Host.Supervision;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Supervision.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Modules.Job.Contracts.Queries;

/// <summary>
/// Témoin de vie de la supervision (FIX210, F12 §5.1). L'évaluation du dead-man's-switch est un job SYSTÈME
/// (<see cref="SupervisionEvaluationTrigger"/>, fan-out cross-tenant) : ses exécutions vivent dans la base
/// SYSTÈME. On les lit via le Contract du module Job (<see cref="IJobQueries"/>) dans un scope SYSTÈME — un
/// scope DI neuf sans tenant ambiant route la connexion vers la base système (même mécanique que l'amorçage
/// au démarrage). La dernière exécution « Completed » de ce type donne la dernière évaluation ; au-delà du
/// double de la cadence (F12 §5.1), le dispositif est jugé en retard. La lecture est best-effort : un échec
/// rend un état « indéterminé », jamais une fausse alerte.
/// </summary>
internal sealed partial class SupervisionLivenessProvider : ISupervisionLivenessProvider
{
    /// <summary>Statut persistant d'un job terminé avec succès (valeur stable de <c>JobStatus.Completed</c>).</summary>
    private const string CompletedStatus = "Completed";

    /// <summary>Plafond de lecture des jobs terminés récents (toutes natures) — l'évaluation tourne toutes les
    /// 15 min, sa dernière exécution est donc dans une fenêtre courte ; un volume non couvert dégrade
    /// prudemment vers « en retard » (jamais un faux « sain »).</summary>
    private const int CompletedJobScanLimit = 200;

    private static readonly string SupervisionJobType = typeof(SupervisionEvaluationTrigger).FullName!;

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
            // Scope DI neuf SANS tenant ambiant ⇒ IConnectionFactory route vers la base SYSTÈME (où vivent les
            // jobs système). Lecture par le Contract du module Job (jamais de SQL maison cross-module, CLAUDE.md n°14).
            await using var scope = _scopeFactory.CreateAsyncScope();
            var jobQueries = scope.ServiceProvider.GetRequiredService<IJobQueries>();
            var completed = await jobQueries.ListByStatusAsync(CompletedStatus, CompletedJobScanLimit, cancellationToken)
                .ConfigureAwait(false);

            lastEvaluationUtc = completed
                .Where(j => string.Equals(j.Type, SupervisionJobType, StringComparison.Ordinal) && j.CompletedAt is not null)
                .Select(j => j.CompletedAt)
                .Max();
        }
        catch (Exception ex)
        {
            // Best-effort : un échec de lecture ne doit jamais bloquer la page ni afficher une fausse alerte.
            LogLivenessReadFailed(_logger, ex);
            return new SupervisionLivenessView
            {
                LastEvaluationUtc = null,
                Status = SupervisionLivenessStatus.Unknown,
                IntervalMinutes = AlertDeviceQueries.EvaluationIntervalMinutes,
            };
        }

        return Evaluate(lastEvaluationUtc, _timeProvider.GetUtcNow(), AlertDeviceQueries.EvaluationIntervalMinutes);
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
