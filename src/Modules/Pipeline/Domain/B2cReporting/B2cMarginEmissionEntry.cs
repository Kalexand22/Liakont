namespace Liakont.Modules.Pipeline.Domain.B2cReporting;

using System;

/// <summary>
/// Entrée APPEND-ONLY du journal d'émission e-reporting B2C de la marge (flux 10.3) — l'état d'émission
/// PERSISTANT qui porte l'anti-doublon côté produit (l'API SuperPDP n'a aucune clé d'idempotence). Le grain
/// est le <b>document marge</b> (décision Karl D3 : attempt-once par document) : chaque document tenté écrit
/// une entrée <see cref="B2cMarginEmissionStatus.Pending"/> AVANT le POST puis une entrée d'issue après —
/// un document portant déjà une entrée n'est jamais re-tenté en auto (crash-safe, jamais 2 POST).
/// <para>Montants/forme ne sont PAS ici : le journal trace l'AGRÉGAT auquel le document a contribué
/// (jour×devise×catégorie×rôle + empreinte de contenu) pour l'audit, jamais une règle fiscale (CLAUDE.md n°2).</para>
/// </summary>
public sealed record B2cMarginEmissionEntry
{
    /// <summary>Document marge source de cette contribution (clé d'attempt-once, décision D3).</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Référence de la pièce source (audit/réversibilité, ADR-0007).</summary>
    public required string SourceReference { get; init; }

    /// <summary>Jour de l'agrégat auquel ce document a contribué (grain jour×devise, F03 §2.5).</summary>
    public required DateOnly AggregateDate { get; init; }

    /// <summary>Devise ISO 4217 de l'agrégat.</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>Code catégorie de transaction de l'agrégat (TT-81, canonique — ex. <c>TMA1</c>).</summary>
    public required string Category { get; init; }

    /// <summary>Code rôle du déclarant de l'agrégat (TT-15, canonique — ex. <c>SE</c>).</summary>
    public required string Role { get; init; }

    /// <summary>Empreinte déterministe du contenu de l'agrégat transmis (audit ; pas une clé d'idempotence — l'anti-doublon est par document).</summary>
    public required string ContentHash { get; init; }

    /// <summary>Issue de la tentative.</summary>
    public required B2cMarginEmissionStatus Status { get; init; }

    /// <summary>Identifiant serveur de la transaction côté PA (présent quand <see cref="B2cMarginEmissionStatus.Issued"/>), sinon <c>null</c>.</summary>
    public string? PaEmissionId { get; init; }

    /// <summary>Réponse brute de la PA (audit) ; peut ne pas être du JSON — conservée telle quelle, jamais réinterprétée.</summary>
    public string? PaResponseSnapshot { get; init; }

    /// <summary>Message opérateur (français) pour une issue non terminale (rejet/erreur), ou <c>null</c>.</summary>
    public string? Detail { get; init; }
}
