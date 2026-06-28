namespace Liakont.Modules.Pipeline.Contracts.Jobs;

/// <summary>
/// Déclencheur de l'e-reporting B2C de la MARGE (flux 10.3, enchères — B4) : charge utile d'un job SYSTÈME que
/// l'ordonnanceur (cron — geste opérateur) ou l'API publie. Le handler de fan-out (un seul, côté plateforme)
/// résout chaque tenant ACTIF via <c>ITenantJobRunner</c> (SOL06) et exécute le job une fois par tenant —
/// JAMAIS une boucle multi-tenant maison. La garantie « un seul job par tenant à la fois » est portée par
/// l'ordonnanceur, pas par un mutex applicatif.
/// </summary>
/// <remarks>
/// Le job découvre les documents B2C-marge (frais acheteur/vendeur, sans TVA distincte — art. 297 E), résout
/// leur marge (cœur PUR fail-closed), agrège jour×devise×taux et TRANSMET chaque agrégat à la PA. L'anti-doublon
/// est porté par un journal d'émission append-only AU GRAIN DOCUMENT (attempt-once — l'API SuperPDP n'a aucune
/// clé d'idempotence) : sans charge utile variable, pas de dry-run.
/// </remarks>
public sealed record AggregateB2cMarginAllTrigger;
