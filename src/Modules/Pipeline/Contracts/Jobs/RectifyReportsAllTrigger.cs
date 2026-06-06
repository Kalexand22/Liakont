namespace Liakont.Modules.Pipeline.Contracts.Jobs;

/// <summary>
/// Déclencheur de la ré-évaluation des rectificatifs d'e-reporting (PIP04, flux RE) : charge utile d'un job
/// SYSTÈME que l'ordonnanceur (cron — geste opérateur) ou l'API publie. Le handler de fan-out (un seul, côté
/// plateforme) résout chaque tenant ACTIF via <c>ITenantJobRunner</c> (SOL06) et ré-évalue, pour chacun, les
/// périodes DÉJÀ DÉCLARÉES — JAMAIS une boucle multi-tenant maison. La garantie « un seul job par tenant à la
/// fois » est portée par l'ordonnanceur, pas par un mutex applicatif.
/// </summary>
/// <remarks>
/// Une correction amont (avoir sur période déclarée — PIP02 ; altération source détectée — TRK03) modifie la
/// projection d'agrégation : la ré-évaluation détecte le changement d'empreinte et transmet un rectificatif RE
/// (annule-et-remplace), IDEMPOTENT (un contenu inchangé ne re-transmet pas). Le rectificatif manuel d'un
/// opérateur appelle directement le service de rectification pour une période ciblée.
/// </remarks>
public sealed record RectifyReportsAllTrigger;
