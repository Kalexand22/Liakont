namespace Liakont.Host.Startup;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Modules.Job.Application;

/// <summary>
/// Diagnostic de démarrage (FIX203b, même pattern que <see cref="DevRealmHealthCheck"/>) : avertit si
/// un job SYSTÈME attendu (<see cref="SystemJobDefinitions"/>) n'a AUCUN schedule actif. C'est la cause
/// du « supervision morte en silence » / « coffre jamais ancré » constaté en recette run 2 :
/// <c>job.schedules</c> vide ⇒ le dead-man's-switch et l'ancrage ne tournent jamais, sans aucun signal.
/// <para>
/// S'exécute en DEV comme en PROD (le risque de planification absente est surtout un risque de prod),
/// best-effort : il ne BLOQUE jamais le démarrage (base indisponible/au démarrage = silencieux) et ne
/// fait que lire les types de jobs actifs (<c>GetActiveJobTypesAsync</c>, sans verrou). En dev,
/// <see cref="DevJobScheduleSeeder"/> a déjà amorcé ces planifications ; en prod, c'est un geste OPS.
/// </para>
/// </summary>
internal static partial class SystemJobScheduleHealthCheck
{
    /// <summary>Émet un avertissement par job système attendu qui n'a aucune planification active.</summary>
    public static async Task WarnIfSystemJobsUnscheduledAsync(this WebApplication app)
    {
        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Liakont.Host.Startup.SystemJobScheduleHealthCheck");

        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var uowFactory = scope.ServiceProvider.GetRequiredService<IScheduleUnitOfWorkFactory>();
            await using var uow = await uowFactory.BeginAsync();

            var activeJobTypes = await uow.GetActiveJobTypesAsync();

            foreach (var job in FindMissing(SystemJobDefinitions.All, activeJobTypes))
            {
                if (job.Class == SystemJobClass.RequiredSeeded && job.CronExpression is { } cron)
                {
                    LogSystemJobUnscheduled(logger, job.Label, cron);
                }
                else
                {
                    // Cadence de déploiement (RDL07/A6-cons-2) : récurrent mais aucune cadence sourcée — on
                    // signale sans suggérer de cron (à planifier SI la fonctionnalité est utilisée).
                    LogDeploymentCadenceJobUnscheduled(logger, job.Label);
                }
            }
        }
        catch (Exception ex)
        {
            // Best-effort : base non migrée / indisponible au démarrage — on ne bloque pas le boot.
            LogHealthCheckSkipped(logger, ex);
        }
    }

    /// <summary>
    /// Décision PURE (testable) : les jobs système attendus dont le type n'a AUCUN schedule actif.
    /// Comparaison par nom complet de type (clé technique exacte), sensible à la casse.
    /// </summary>
    internal static IReadOnlyList<SystemJobDefinition> FindMissing(
        IReadOnlyList<SystemJobDefinition> expected,
        IReadOnlyCollection<string> activeJobTypes)
    {
        var present = new HashSet<string>(activeJobTypes, StringComparer.Ordinal);
        var missing = new List<SystemJobDefinition>();
        foreach (var job in expected)
        {
            if (!present.Contains(job.JobType))
            {
                missing.Add(job);
            }
        }

        return missing;
    }

    [LoggerMessage(
        EventId = 7220,
        Level = LogLevel.Warning,
        Message = "Job SYSTÈME attendu SANS planification active : {Label}. Il ne s'exécutera JAMAIS "
            + "(supervision/ancrage muets). Créez-le via l'admin des planifications (cron suggéré « {Cron} », UTC) "
            + "— en dev, le seed le pose automatiquement.")]
    private static partial void LogSystemJobUnscheduled(ILogger logger, string label, string cron);

    [LoggerMessage(
        EventId = 7222,
        Level = LogLevel.Warning,
        Message = "Job SYSTÈME de fan-out récurrent SANS planification active : {Label}. SI vous utilisez la "
            + "fonctionnalité correspondante, planifiez-le via l'admin des planifications (sa cadence relève de "
            + "votre déploiement, UTC) — sinon il ne s'exécutera JAMAIS (job mort en production).")]
    private static partial void LogDeploymentCadenceJobUnscheduled(ILogger logger, string label);

    [LoggerMessage(
        EventId = 7221,
        Level = LogLevel.Debug,
        Message = "Vérification des planifications système ignorée (base indisponible au démarrage) — best-effort.")]
    private static partial void LogHealthCheckSkipped(ILogger logger, Exception exception);
}
