namespace Liakont.Modules.Mandats.Domain.Entities;

/// <summary>
/// Résultat d'une allocation de <see cref="MandatSequence"/> : la valeur brute <c>bigint</c> consommée
/// (<see cref="Value"/>, pour l'ordre/audit) et le BT-1 fiscal RENDU (<see cref="FormattedNumber"/> = préfixe
/// du mandant + valeur), assigné à l'émission HORS du payload hashé (INV-BT1-1, ADR-0025).
/// </summary>
public readonly record struct MandatSequenceAllocation(long Value, string FormattedNumber);
