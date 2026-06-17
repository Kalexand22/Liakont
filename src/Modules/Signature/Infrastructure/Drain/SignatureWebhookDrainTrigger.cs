namespace Liakont.Modules.Signature.Infrastructure.Drain;

/// <summary>
/// Déclencheur du job SYSTÈME de drain des webhooks de signature (ADR-0029 §4/§5). Son handler
/// <see cref="SignatureWebhookDrainFanOutHandler"/> fait le fan-out sur tous les tenants actifs via
/// <c>ITenantJobRunner</c> (SOL06). Marqueur sans donnée : le job draine l'inbox de chaque tenant.
/// <para>
/// La CADENCE du drain est un geste de planification opérateur (comme la bascule tacite MND04 et le
/// récapitulatif SUP03) — pas une cadence fiscale inventée (CLAUDE.md n°2). Le handler est enregistré (le job
/// est invocable) ; sa planification reste libre.
/// </para>
/// </summary>
public sealed record SignatureWebhookDrainTrigger;
