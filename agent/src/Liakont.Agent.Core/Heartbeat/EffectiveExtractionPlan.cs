namespace Liakont.Agent.Core.Heartbeat;

using System;
using System.Collections.Generic;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Configuration;

/// <summary>
/// Plan d'extraction EFFECTIF après pilotage centralisé (F12 §2.5, §6.1 décision n°3 « la
/// planification poussée par la plateforme surcharge le fichier local »). Résolution PURE et
/// déterministe à partir de la configuration locale (<see cref="ExtractionConfig"/>) et de la dernière
/// configuration plateforme connue (<see cref="AgentConfigurationDto"/>, éventuellement <c>null</c>
/// quand la plateforme n'a jamais été jointe — l'agent extrait alors selon le fichier local, F12 §2.5).
/// <para>
/// Règle de surcharge : la planification plateforme (<see cref="AgentConfigurationDto.ExtractionSchedule"/>)
/// PRIME sur le fichier local QUAND ELLE EST PRÉSENTE ; sinon le fichier local gouverne. La fenêtre
/// imposée (<see cref="AgentConfigurationDto.ExtractFromUtc"/> / <see cref="AgentConfigurationDto.ExtractToUtc"/>)
/// est portée par la plateforme indépendamment de la source de planification. Le rattrapage au
/// démarrage (<see cref="ExtractionConfig.CatchUpOnStart"/>) reste un réglage LOCAL (la plateforme ne
/// le surcharge pas).
/// </para>
/// </summary>
public sealed class EffectiveExtractionPlan
{
    private EffectiveExtractionPlan(
        ExtractionScheduleSource scheduleSource,
        string? platformSchedule,
        IReadOnlyList<string> localSchedule,
        bool catchUpOnStart,
        DateTime? imposedFromUtc,
        DateTime? imposedToUtc)
    {
        ScheduleSource = scheduleSource;
        PlatformSchedule = platformSchedule;
        LocalSchedule = localSchedule;
        CatchUpOnStart = catchUpOnStart;
        ImposedFromUtc = imposedFromUtc;
        ImposedToUtc = imposedToUtc;
    }

    /// <summary>Qui gouverne la planification : le fichier local ou la plateforme.</summary>
    public ExtractionScheduleSource ScheduleSource { get; }

    /// <summary>Planification plateforme (expression cron) — gouverne ssi <see cref="ScheduleSource"/> = Platform.</summary>
    public string? PlatformSchedule { get; }

    /// <summary>Planification locale (heures <c>HH:mm</c>) — gouverne ssi <see cref="ScheduleSource"/> = Local.</summary>
    public IReadOnlyList<string> LocalSchedule { get; }

    /// <summary>Rattrapage au démarrage (réglage local, jamais surchargé par la plateforme).</summary>
    public bool CatchUpOnStart { get; }

    /// <summary>Borne basse imposée par la plateforme (UTC, incluse), si présente.</summary>
    public DateTime? ImposedFromUtc { get; }

    /// <summary>Borne haute imposée par la plateforme (UTC, exclue), si présente.</summary>
    public DateTime? ImposedToUtc { get; }

    /// <summary>La planification est pilotée par la plateforme (et non par le fichier local).</summary>
    public bool IsPlatformControlled => ScheduleSource == ExtractionScheduleSource.Platform;

    /// <summary>
    /// Résout le plan effectif. <paramref name="platform"/> = <c>null</c> (plateforme jamais jointe)
    /// donne un plan 100 % local — l'agent extrait selon son fichier, sans rien inventer (CLAUDE.md n°2).
    /// </summary>
    /// <param name="local">Configuration locale d'extraction (jamais nulle).</param>
    /// <param name="platform">Dernière configuration plateforme connue, ou <c>null</c>.</param>
    /// <returns>Le plan d'extraction effectif.</returns>
    public static EffectiveExtractionPlan Resolve(ExtractionConfig local, AgentConfigurationDto? platform)
    {
        if (local is null)
        {
            throw new ArgumentNullException(nameof(local));
        }

        bool platformScheduleGoverns = !string.IsNullOrWhiteSpace(platform?.ExtractionSchedule);

        return new EffectiveExtractionPlan(
            scheduleSource: platformScheduleGoverns ? ExtractionScheduleSource.Platform : ExtractionScheduleSource.Local,
            platformSchedule: platformScheduleGoverns ? platform!.ExtractionSchedule : null,
            localSchedule: local.Schedule,
            catchUpOnStart: local.CatchUpOnStart,
            imposedFromUtc: platform?.ExtractFromUtc,
            imposedToUtc: platform?.ExtractToUtc);
    }
}
