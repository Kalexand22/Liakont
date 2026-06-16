namespace Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;

/// <summary>
/// Déclencheur du job SYSTÈME de bascule tacite des acceptations 389 (MND04). Son handler
/// <see cref="SelfBilledAcceptanceTacitFanOutHandler"/> fait le fan-out sur tous les tenants actifs via le
/// runner (SOL06). Marqueur sans donnée : le job agit sur les acceptations dues de chaque tenant.
/// <para>
/// La CADENCE du balayage n'est PAS fixée par la spec (F15 §2.3 ne fixe que l'échéance par document,
/// <c>DeadlineUtc</c>) : aucune cadence n'est inventée ici (CLAUDE.md n°2). Le handler est enregistré
/// (le job est invocable), mais sa planification reste un geste opérateur — pas d'entrée
/// <c>SystemJobDefinitions</c> (même posture que le récapitulatif SUP03, optionnel/non amorcé).
/// </para>
/// </summary>
public sealed record SelfBilledAcceptanceTacitTrigger;
