namespace Liakont.Modules.Reference.Contracts.Commands;

using MediatR;

/// <summary>
/// Supprime une correspondance de code pays (ADR-0038). La suppression est journalisée append-only (auteur
/// + valeur avant) dans la MÊME transaction que la suppression. Sans effet si le code source n'existe pas.
/// </summary>
public sealed record RemoveCountryAliasCommand : IRequest
{
    /// <summary>Code source de la correspondance à retirer (normalisé à l'écriture).</summary>
    public required string SourceCode { get; init; }
}
