namespace Liakont.Modules.Reference.Contracts.Commands;

using MediatR;

/// <summary>
/// Ajoute ou met à jour une correspondance de code pays (ADR-0038). La cible <see cref="IsoCode"/> DOIT
/// être un vrai code ISO 3166-1 alpha-2 : elle est validée à l'écriture (un code garbage est refusé, jamais
/// stocké puis laissé bloquer en aval par BT-55 — INV-REF-CTRY-03). <see cref="SourceCode"/> est normalisé
/// (sans espaces, MAJUSCULES) comme clé. La mutation est journalisée append-only (auteur + avant/après) dans
/// la MÊME transaction que l'upsert (CLAUDE.md n°4).
/// </summary>
public sealed record UpsertCountryAliasCommand : IRequest
{
    /// <summary>Code source à mapper (ex. « BEL »). Normalisé à l'écriture.</summary>
    public required string SourceCode { get; init; }

    /// <summary>Code ISO 3166-1 alpha-2 cible (ex. « BE »). Validé ISO à l'écriture.</summary>
    public required string IsoCode { get; init; }
}
