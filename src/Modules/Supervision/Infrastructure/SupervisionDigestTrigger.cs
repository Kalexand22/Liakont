namespace Liakont.Modules.Supervision.Infrastructure;

/// <summary>
/// Déclencheur du récapitulatif quotidien (digest) des alertes actives (SUP03 §3, OPTIONNEL). Planifié par
/// le module <c>Job</c> (JobScheduler, base système) — la fréquence (quotidienne) est un paramétrage de
/// déploiement créé par l'opérateur via l'admin des schedules, comme l'ancrage quotidien (TRK06). Son
/// handler <see cref="SupervisionDigestFanOutHandler"/> fait le fan-out du digest sur tous les tenants via
/// le runner. Le digest n'est réellement envoyé que si <c>SupervisionNotificationOptions.DailyDigestEnabled</c>
/// est activé (sinon le job tenant est un no-op) : la planification peut exister sans que le digest soit actif.
/// </summary>
public sealed record SupervisionDigestTrigger;
