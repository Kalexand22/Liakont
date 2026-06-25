namespace Liakont.Modules.Pipeline.Contracts.Jobs;

/// <summary>
/// Déclencheur de l'e-reporting B2C au RÉGIME DU PRIX TOTAL taxable (flux 10.3, enchères — catégorie TLB1,
/// BUG-8) : charge utile d'un job SYSTÈME que l'ordonnanceur (cron — geste opérateur) ou l'API publie. Le
/// handler de fan-out (un seul, côté plateforme) résout chaque tenant ACTIF via <c>ITenantJobRunner</c> (SOL06)
/// et exécute le job une fois par tenant — JAMAIS une boucle multi-tenant maison. La garantie « un seul job par
/// tenant à la fois » est portée par l'ordonnanceur, pas par un mutex applicatif.
/// </summary>
/// <remarks>
/// Le job découvre les documents B2C au régime du prix total (commettant assujetti, acheteur particulier, TVA
/// distincte — F03 §2.7), résout leur base (adjudication HT/TVA sourcée + commission acheteur TTC ramenée HT),
/// agrège jour×devise×taux et TRANSMET chaque agrégat à la PA sous TLB1/SE. L'anti-doublon est porté par le
/// MÊME journal d'émission append-only AU GRAIN DOCUMENT que la marge (attempt-once — l'API SuperPDP n'a aucune
/// clé d'idempotence) : sans charge utile variable, pas de dry-run.
/// </remarks>
public sealed record AggregateB2cTaxableAllTrigger;
