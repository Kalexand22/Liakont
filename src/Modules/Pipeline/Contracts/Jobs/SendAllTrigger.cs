namespace Liakont.Modules.Pipeline.Contracts.Jobs;

/// <summary>
/// Déclencheur du pipeline SEND (PIP01c) : charge utile d'un job SYSTÈME que l'ordonnanceur (cron — geste
/// opérateur) ou l'API (API02 « send / send-all ») publie. Le handler de fan-out (un seul, côté plateforme)
/// résout chaque tenant ACTIF via <c>ITenantJobRunner</c> (SOL06) et exécute le SEND une fois par tenant —
/// JAMAIS une boucle multi-tenant maison. La garantie « un seul job send/sync par tenant à la fois » est
/// portée par l'ordonnanceur, pas par un mutex applicatif.
/// </summary>
/// <param name="DryRun">
/// Si <c>true</c>, le SEND simule (« tout sauf écritures PA ») : il dénombre les documents prêts mais
/// n'appelle aucune écriture côté Plateforme Agréée et ne fait avancer aucun document. Utilisé pour valider
/// le paramétrage avant un envoi réel.
/// </param>
public sealed record SendAllTrigger(bool DryRun = false);
