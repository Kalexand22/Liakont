namespace Liakont.Modules.Pipeline.Contracts.Jobs;

/// <summary>
/// Déclencheur de l'e-reporting B2C d'EXPORT HORS UE détaxé (flux 10.3, enchères — catégorie TLB1 UNITAIRE,
/// art. 262 I, BUG-11) : charge utile d'un job SYSTÈME que l'ordonnanceur (cron — geste opérateur) ou l'API
/// publie. Le handler de fan-out (un seul, côté plateforme) résout chaque tenant ACTIF via
/// <c>ITenantJobRunner</c> (SOL06) et exécute le job une fois par tenant — JAMAIS une boucle multi-tenant
/// maison. La garantie « un seul job par tenant à la fois » est portée par l'ordonnanceur, pas par un mutex
/// applicatif.
/// </summary>
/// <remarks>
/// Le job découvre les documents B2C d'export hors UE (acheteur particulier, adjudication mappée catégorie
/// <c>G</c>, art. 262 I — F03 §2.8), constitue UNE transaction e-reporting PAR opération (base HT exonérée =
/// adjudication HT + commission acheteur, taux 0) et TRANSMET chaque opération à la PA sous TLB1/SE. À la
/// différence de la marge/prix total, l'export est UNITAIRE (jamais agrégé jour×devise). L'anti-doublon est
/// porté par le MÊME journal d'émission append-only au grain document que la marge (attempt-once — l'API
/// SuperPDP n'a aucune clé d'idempotence) : sans charge utile variable, pas de dry-run.
/// </remarks>
public sealed record AggregateB2cExportAllTrigger;
