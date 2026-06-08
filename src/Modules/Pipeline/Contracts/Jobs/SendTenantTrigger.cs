namespace Liakont.Modules.Pipeline.Contracts.Jobs;

/// <summary>
/// Déclencheur d'envoi MONO-TENANT (ADR-0016) : charge utile d'un job SYSTÈME publié par une ACTION de la
/// console (API02a « send », « send-all », « runs/trigger ») pour le SEUL tenant de l'opérateur. À la
/// différence de <see cref="SendAllTrigger"/> (fan-out tous-tenants, réservé à l'ordonnanceur d'instance), le
/// handler exécute le SEND pour le seul <see cref="TenantId"/> via <c>ITenantScopeFactory.Create</c> (SOL06,
/// ADR-0006) — JAMAIS <c>RunForAllTenantsAsync</c>. C'est ce qui garantit l'isolation tenant d'une action
/// d'opérateur (CLAUDE.md n°9 ; blueprint.md §7) : une action du tenant A ne transmet jamais les documents
/// des tenants B, C…
/// </summary>
/// <param name="TenantId">
/// Le tenant cible (celui de l'opérateur, renseigné depuis <c>actor.TenantId</c>). Le handler système rétablit
/// ce tenant via <c>ITenantScopeFactory.Create(TenantId)</c> au moment de l'exécution ; il n'est jamais déduit
/// du routage de connexion (le job vit sur la queue SYSTÈME, pas dans la base du tenant).
/// </param>
/// <param name="DryRun">
/// Si <c>true</c>, le SEND simule (« tout sauf écritures PA ») : il dénombre les documents prêts mais n'appelle
/// aucune écriture côté Plateforme Agréée et ne fait avancer aucun document. Sert à valider le paramétrage.
/// </param>
public sealed record SendTenantTrigger(string TenantId, bool DryRun = false);
