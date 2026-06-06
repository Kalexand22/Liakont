namespace Liakont.Modules.Pipeline.Contracts.Jobs;

/// <summary>
/// Déclencheur du pipeline SYNC (PIP01d) : charge utile d'un job SYSTÈME que l'ordonnanceur (cron — geste
/// opérateur) publie. Le handler de fan-out (un seul, côté plateforme) résout chaque tenant ACTIF via
/// <c>ITenantJobRunner</c> (SOL06) et exécute le SYNC une fois par tenant — JAMAIS une boucle multi-tenant
/// maison. La garantie « un seul job send/sync par tenant à la fois » est portée par l'ordonnanceur, pas par
/// un mutex applicatif.
/// </summary>
/// <remarks>
/// Le SYNC est un job de RÉCONCILIATION en lecture seule côté plateforme : pour chaque document déjà émis, il
/// récupère auprès de la Plateforme Agréée — SELON SES CAPACITÉS DÉCLARÉES, jamais un <c>if (pa is …)</c> — la
/// facture électronique générée et le(s) tax report(s) DGFiP, et les ajoute en ADDENDA chaînés au paquet WORM
/// du document (TRK05). Il n'effectue aucune transmission et ne fait avancer aucune machine à états. Sans charge
/// utile variable (pas de dry-run) : un SYNC est idempotent par construction (les addenda sont adressés par
/// empreinte de contenu).
/// </remarks>
public sealed record SyncAllTrigger;
