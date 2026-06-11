namespace Liakont.Host.Startup;

using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stratum.Modules.Job.Contracts.Commands;

/// <summary>
/// Amorçage DEV des planifications des jobs SYSTÈME (FIX203b). La recette run 2 a révélé que
/// <c>job.schedules</c> reste VIDE après un bring-up : la supervision (dead-man's-switch) et l'ancrage
/// quotidien du coffre ne sont jamais déclenchés. En Development, ce seed crée leurs planifications
/// (<see cref="SystemJobDefinitions"/>) pour que l'environnement de dev tourne sans geste manuel.
/// <para>
/// CREATE-ONLY : un schedule déjà présent (même nom + company) n'est jamais écrasé
/// (<c>INV-JOB-005</c> avalé). Best-effort total : ne fait JAMAIS planter le démarrage. En PRODUCTION,
/// la planification reste un geste opérateur via l'admin des planifications (documenté dans
/// <c>deploy/docker/README.md</c>) — ce seed ne s'exécute qu'en Development.
/// </para>
/// </summary>
internal static partial class DevJobScheduleSeeder
{
    /// <summary>Amorce (Development uniquement, create-only) les planifications des jobs système.</summary>
    public static async Task SeedDevJobSchedulesAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Liakont.Host.Startup.DevJobScheduleSeeder");

        // Société fictive de dev : porte les planifications système en dev (le fan-out par tenant est
        // assuré par les handlers, indépendamment de cette company — la clé est juste une scope de schedule).
        var companyIdRaw = app.Configuration["DevTenantSeed:CompanyId"];
        if (!Guid.TryParse(companyIdRaw, out var companyId))
        {
            LogSeedSkippedNoCompany(logger, companyIdRaw ?? "(absent)");
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        foreach (var job in SystemJobDefinitions.All)
        {
            await TrySeedScheduleAsync(sender, job, companyId, logger);
        }
    }

    /// <summary>
    /// Crée une planification (create-only, idempotent). Un schedule de même nom pour la company existe
    /// déjà (re-boot) ⇒ <c>INV-JOB-005</c> avalé, l'existant n'est PAS écrasé. Tout autre échec est
    /// journalisé sans propager (le démarrage ne doit jamais planter pour un seed de dev).
    /// </summary>
    internal static async Task TrySeedScheduleAsync(
        ISender sender,
        SystemJobDefinition job,
        Guid companyId,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            await sender.Send(
                new CreateScheduleCommand
                {
                    Name = job.ScheduleName,
                    CronExpression = job.CronExpression,
                    JobType = job.JobType,
                    CompanyId = companyId,
                },
                ct);

            LogScheduleSeeded(logger, job.ScheduleName, job.CronExpression);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("INV-JOB-005", StringComparison.Ordinal))
        {
            // Déjà planifié : create-only, on n'écrase pas l'existant.
            LogScheduleAlreadyPresent(logger, job.ScheduleName);
        }
        catch (Exception ex)
        {
            LogScheduleSeedFailed(logger, job.ScheduleName, ex);
        }
    }

    [LoggerMessage(
        EventId = 7210,
        Level = LogLevel.Information,
        Message = "Planification système amorcée (dev) : « {Name} » ({Cron}, UTC).")]
    private static partial void LogScheduleSeeded(ILogger logger, string name, string cron);

    [LoggerMessage(
        EventId = 7211,
        Level = LogLevel.Debug,
        Message = "Planification système « {Name} » déjà présente — amorçage ignoré (create-only).")]
    private static partial void LogScheduleAlreadyPresent(ILogger logger, string name);

    [LoggerMessage(
        EventId = 7212,
        Level = LogLevel.Warning,
        Message = "Échec de l'amorçage de la planification système « {Name} » (dev) — le démarrage se poursuit.")]
    private static partial void LogScheduleSeedFailed(ILogger logger, string name, Exception exception);

    [LoggerMessage(
        EventId = 7213,
        Level = LogLevel.Debug,
        Message = "Amorçage des planifications système ignoré : DevTenantSeed:CompanyId absent ou invalide ({Raw}).")]
    private static partial void LogSeedSkippedNoCompany(ILogger logger, string raw);
}
