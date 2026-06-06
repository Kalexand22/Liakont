namespace Liakont.Modules.Pipeline.Contracts.Jobs;

/// <summary>
/// Déclencheur de l'agrégation de paiement (PIP03a, F09 §2) : charge utile d'un job SYSTÈME que
/// l'ordonnanceur (cron — geste opérateur) ou l'API publie. Le handler de fan-out (un seul, côté plateforme)
/// résout chaque tenant ACTIF via <c>ITenantJobRunner</c> (SOL06) et exécute l'agrégation une fois par
/// tenant — JAMAIS une boucle multi-tenant maison. La garantie « un seul job par tenant à la fois » est
/// portée par l'ordonnanceur, pas par un mutex applicatif.
/// </summary>
/// <remarks>
/// L'agrégation est un calcul de PROJECTION en lecture seule des encaissements + snapshots de ventilation
/// (ADR-0015) : elle ne transmet rien et ne fait avancer aucune machine à états (le fenêtrage en période et
/// l'envoi réel sont PIP03b). Idempotente par construction (la projection est recalculée et upsertée par
/// jour×taux) — sans charge utile variable, pas de dry-run.
/// </remarks>
public sealed record AggregatePaymentsAllTrigger;
