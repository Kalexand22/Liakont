namespace Liakont.Modules.Pipeline.Contracts.Jobs;

/// <summary>
/// Déclencheur de l'e-reporting B2C des documents ORDINAIRES taxables (flux 10.3, hors enchères — facture client
/// (TLB1) / note d'honoraires (TPS1), F03 §2.9, #7) : charge utile d'un job SYSTÈME que l'ordonnanceur (cron —
/// geste opérateur) ou l'API publie. Le handler de fan-out (un seul, côté plateforme) résout chaque tenant ACTIF
/// via <c>ITenantJobRunner</c> (SOL06) et exécute le job une fois par tenant — JAMAIS une boucle multi-tenant
/// maison. La garantie « un seul job par tenant à la fois » est portée par l'ordonnanceur, pas par un mutex applicatif.
/// </summary>
/// <remarks>
/// Le job découvre les documents B2C ORDINAIRES (acheteur particulier, lignes taxables, AUCUN frais d'enchères —
/// F03 §2.9), agrège jour×devise×taux PAR catégorie de transaction (TLB1 livraison de biens / TPS1 prestation de
/// services, dérivée de la nature de l'opération) et TRANSMET chaque agrégat à la PA sous SE. L'anti-doublon est
/// porté par le MÊME journal d'émission append-only au grain document que les autres flux B2C (attempt-once —
/// l'API SuperPDP n'a aucune clé d'idempotence) : sans charge utile variable, pas de dry-run.
/// </remarks>
public sealed record AggregateB2cPlainTaxableAllTrigger;
