namespace Liakont.Modules.Documents.Domain.Entities;

using System;

/// <summary>
/// Triplet de preuve d'un document ÉMIS (F06 §3, item TRK04) : les trois snapshots constituent la preuve
/// complète qu'un contrôle fiscal peut exiger des années après — le <see cref="PayloadSnapshot"/> transmis
/// à la Plateforme Agréée, la <see cref="PaResponseSnapshot"/> brute (avec les identifiants DGFiP), et la
/// <see cref="MappingTrace"/> TVA appliquée (quelle règle, quelle version — F03). Les trois sont OBLIGATOIRES :
/// on ne peut pas enregistrer une émission sans sa preuve complète. Portés par le <see cref="DocumentEvent"/>
/// de l'émission (colonnes jsonb append-only), ils ne sont jamais modifiés après écriture (CLAUDE.md n°4).
/// </summary>
public sealed class IssuanceSnapshots
{
    /// <summary>Construit le triplet de preuve d'une émission. Chaque snapshot est un document JSON non vide (F06 §3).</summary>
    /// <param name="payloadSnapshot">Snapshot du payload pivot transmis (JSON non vide).</param>
    /// <param name="paResponseSnapshot">Snapshot de la réponse brute de la PA (JSON non vide).</param>
    /// <param name="mappingTrace">Trace de mapping TVA appliquée (JSON non vide).</param>
    /// <param name="paDocumentId">
    /// Identifiant du document attribué par la Plateforme Agréée à l'émission, clé de récupération aval (facture
    /// générée, tax reports — SYNC/PIP01d). Optionnel : <c>null</c> n'altère pas une référence déjà posée (jamais
    /// un effacement). Ce n'est PAS une « preuve » au sens des trois snapshots (qui restent obligatoires).
    /// </param>
    public IssuanceSnapshots(string payloadSnapshot, string paResponseSnapshot, string mappingTrace, string? paDocumentId = null)
    {
        PayloadSnapshot = RequireSnapshot(
            payloadSnapshot,
            nameof(payloadSnapshot),
            "Le snapshot du payload pivot transmis est obligatoire pour un document émis (preuve d'audit, F06 §3).");
        PaResponseSnapshot = RequireSnapshot(
            paResponseSnapshot,
            nameof(paResponseSnapshot),
            "Le snapshot de la réponse de la Plateforme Agréée est obligatoire pour un document émis (preuve de transmission, F06 §3).");
        MappingTrace = RequireSnapshot(
            mappingTrace,
            nameof(mappingTrace),
            "La trace de mapping TVA est obligatoire pour un document émis (justification de la TVA appliquée, F03/F06 §3).");
        PaDocumentId = paDocumentId;
    }

    /// <summary>Snapshot du payload pivot exact transmis à la Plateforme Agréée (JSON).</summary>
    public string PayloadSnapshot { get; }

    /// <summary>Réponse brute de la Plateforme Agréée, avec les identifiants DGFiP (JSON).</summary>
    public string PaResponseSnapshot { get; }

    /// <summary>Trace de la/des règle(s) de mapping TVA appliquée(s) et de leur version (JSON, F03).</summary>
    public string MappingTrace { get; }

    /// <summary>Identifiant du document côté Plateforme Agréée (clé de récupération aval), ou <c>null</c>.</summary>
    public string? PaDocumentId { get; }

    internal static string RequireSnapshot(string value, string paramName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, paramName);
        }

        return value;
    }
}
