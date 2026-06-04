namespace Liakont.Modules.Payments.Domain.Entities;

using System;

/// <summary>
/// Snapshots de preuve d'une TRANSMISSION d'agrégat de paiement (F06 §3 / F09, item TRK04) : un agrégat
/// transmis à la DGFiP a la même valeur fiscale qu'un document émis — sa transmission archive donc le
/// <see cref="PayloadSnapshot"/> exact envoyé et la <see cref="PaResponseSnapshot"/> brute de la Plateforme
/// Agréée (acceptation comme rejet). Les deux sont OBLIGATOIRES ; portés par le
/// <see cref="PaymentAggregateEvent"/> (colonnes jsonb append-only, jamais modifiées après coup — CLAUDE.md n°4).
/// </summary>
public sealed class AggregateTransmissionSnapshots
{
    /// <summary>Construit les snapshots d'une transmission. Chaque snapshot est un document JSON non vide (F06 §3).</summary>
    public AggregateTransmissionSnapshots(string payloadSnapshot, string paResponseSnapshot)
    {
        PayloadSnapshot = Require(
            payloadSnapshot,
            nameof(payloadSnapshot),
            "Le snapshot du payload d'agrégat transmis est obligatoire (preuve de transmission, F06 §3 / F09).");
        PaResponseSnapshot = Require(
            paResponseSnapshot,
            nameof(paResponseSnapshot),
            "Le snapshot de la réponse de la Plateforme Agréée est obligatoire pour une transmission d'agrégat (F06 §3 / F09).");
    }

    /// <summary>Snapshot du payload d'agrégat exact transmis à la Plateforme Agréée (JSON).</summary>
    public string PayloadSnapshot { get; }

    /// <summary>Réponse brute de la Plateforme Agréée (acceptation ou rejet, JSON).</summary>
    public string PaResponseSnapshot { get; }

    private static string Require(string value, string paramName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, paramName);
        }

        return value;
    }
}
