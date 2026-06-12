namespace Liakont.Modules.FleetSupervision.Application;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Liakont.Modules.FleetSupervision.Contracts;
using Liakont.Modules.FleetSupervision.Contracts.DTOs;

/// <summary>
/// Calcul PUR des alertes de flotte (OPS04) à partir de la dernière télémétrie connue d'une instance, des
/// seuils du central et de la dernière version publiée. Sans état, sans I/O — entièrement testable. Les
/// alertes ne sont pas persistées : elles sont recalculées à la lecture du dashboard et à chaque passe de
/// notification (à la différence des alertes tenant du module Supervision, qui ont un cycle de vie).
/// </summary>
public static class FleetAlertEvaluator
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Calcule les alertes d'UNE instance.</summary>
    public static IReadOnlyList<FleetAlertDto> Evaluate(
        FleetInstanceDto instance,
        FleetAlertThresholds thresholds,
        string latestVersion,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var alerts = new List<FleetAlertDto>();

        // Instance muette : aucun heartbeat reçu depuis plus que le seuil de silence (panne, réseau coupé,
        // ou job d'envoi arrêté). C'est l'inverse du dead-man's-switch tenant : ici le CENTRAL détecte le
        // silence d'une INSTANCE.
        TimeSpan sinceSeen = nowUtc - instance.LastSeenUtc;
        if (sinceSeen > TimeSpan.FromMinutes(thresholds.InstanceMuteThresholdMinutes))
        {
            alerts.Add(new FleetAlertDto
            {
                InstanceId = instance.InstanceId,
                DisplayName = instance.DisplayName,
                Kind = FleetAlertKind.InstanceMute,
                Severity = FleetAlertSeverity.Critical,
                Message = string.Format(
                    Fr,
                    "Instance muette : aucun signe de vie depuis {0}. Dernier heartbeat le {1} UTC.",
                    FormatDuration(sinceSeen),
                    instance.LastSeenUtc.ToString("dd/MM/yyyy HH:mm", Fr)),
            });
        }

        // Sauvegarde en échec : aucune sauvegarde réussie connue, ou trop ancienne (le marqueur de succès
        // n'a pas été rafraîchi). Source = marqueur de l'instance (BackupMarkerPath), pas les internes d'OPS01b.
        bool backupMissing = instance.LastSuccessfulBackupUtc is null;
        bool backupStale = instance.LastSuccessfulBackupUtc is { } backup
            && (nowUtc - backup) > TimeSpan.FromHours(thresholds.BackupMaxAgeHours);
        if (backupMissing || backupStale)
        {
            string detail = backupMissing
                ? "aucune sauvegarde réussie connue"
                : string.Format(Fr, "dernière sauvegarde réussie le {0} UTC", instance.LastSuccessfulBackupUtc!.Value.ToString("dd/MM/yyyy HH:mm", Fr));
            alerts.Add(new FleetAlertDto
            {
                InstanceId = instance.InstanceId,
                DisplayName = instance.DisplayName,
                Kind = FleetAlertKind.BackupFailure,
                Severity = FleetAlertSeverity.Warning,
                Message = string.Format(Fr, "Sauvegarde en échec : {0}.", detail),
            });
        }

        // Version obsolète : l'instance est en retard sur la dernière version publiée (paramétrage du central).
        if (FleetVersion.IsObsolete(instance.Version, latestVersion))
        {
            alerts.Add(new FleetAlertDto
            {
                InstanceId = instance.InstanceId,
                DisplayName = instance.DisplayName,
                Kind = FleetAlertKind.ObsoleteVersion,
                Severity = FleetAlertSeverity.Warning,
                Message = string.Format(
                    Fr,
                    "Version obsolète : {0} (dernière version publiée : {1}).",
                    string.IsNullOrWhiteSpace(instance.Version) ? "inconnue" : instance.Version,
                    latestVersion),
            });
        }

        return alerts;
    }

    /// <summary>Calcule les alertes de TOUTE la flotte (concaténation par instance).</summary>
    public static IReadOnlyList<FleetAlertDto> EvaluateAll(
        IEnumerable<FleetInstanceDto> instances,
        FleetAlertThresholds thresholds,
        string latestVersion,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(instances);
        return instances
            .SelectMany(instance => Evaluate(instance, thresholds, latestVersion, nowUtc))
            .ToList();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalDays >= 1)
        {
            return string.Format(Fr, "{0} j {1} h", (int)duration.TotalDays, duration.Hours);
        }

        if (duration.TotalHours >= 1)
        {
            return string.Format(Fr, "{0} h {1} min", (int)duration.TotalHours, duration.Minutes);
        }

        return string.Format(Fr, "{0} min", (int)duration.TotalMinutes);
    }
}
