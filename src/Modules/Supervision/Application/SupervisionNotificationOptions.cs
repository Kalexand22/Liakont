namespace Liakont.Modules.Supervision.Application;

/// <summary>
/// Paramétrage de NIVEAU INSTANCE des notifications de supervision (SUP03, F12 §5.3 / §6.1) lié depuis la
/// section <c>Supervision:Notifications</c> des appsettings. Distinct du paramétrage SMTP (transport,
/// section <c>Smtp</c>) : ici on déclare QUI reçoit quoi, pas COMMENT on envoie.
/// </summary>
public sealed class SupervisionNotificationOptions
{
    public const string SectionName = "Supervision:Notifications";

    /// <summary>
    /// Email de l'opérateur d'instance (éditeur / IT Innovations) — reçoit TOUTES les alertes (F12 §5.3).
    /// Vide = aucune notification opérateur (les alertes restent visibles au dashboard).
    /// </summary>
    public string OperatorEmail { get; init; } = string.Empty;

    /// <summary>
    /// Envoie aussi un email à la RÉSOLUTION d'une alerte (à l'opérateur). Optionnel (défaut faux) pour
    /// limiter le volume — SUP03 §3 : « un email de résolution optionnel ».
    /// </summary>
    public bool SendResolutionEmails { get; init; }

    /// <summary>
    /// Active le récapitulatif quotidien (digest) des alertes ACTIVES à l'opérateur. Optionnel (défaut
    /// faux) — SUP03 §3 : « Récapitulatif quotidien optionnel ». La planification (cron) est créée par
    /// l'opérateur via l'admin des schedules, comme les autres jobs récurrents.
    /// </summary>
    public bool DailyDigestEnabled { get; init; }
}
