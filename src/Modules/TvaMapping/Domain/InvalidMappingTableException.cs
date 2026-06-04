namespace Liakont.Modules.TvaMapping.Domain;

using Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Levée quand une table de mapping TVA viole une règle structurelle, à la création (écriture) ou
/// au chargement (item TVA01 §3/§4). Le produit BLOQUE plutôt que d'accepter une table fausse
/// (CLAUDE.md n°3) : aucun comportement silencieux. Le message est destiné à l'opérateur (français,
/// avec l'action corrective — CLAUDE.md n°12).
/// </summary>
public sealed class InvalidMappingTableException : DomainException
{
    /// <summary>Crée l'exception à partir de la liste des violations structurelles détectées.</summary>
    /// <param name="violations">Violations détectées (au moins une).</param>
    public InvalidMappingTableException(IReadOnlyList<string> violations)
        : base(BuildMessage(violations))
    {
        Violations = violations;
    }

    /// <summary>Liste des violations structurelles ayant invalidé la table.</summary>
    public IReadOnlyList<string> Violations { get; }

    private static string BuildMessage(IReadOnlyList<string> violations)
    {
        var detail = violations is { Count: > 0 }
            ? string.Join(" ; ", violations)
            : "raison non précisée";

        return "La table de mapping TVA est invalide et ne peut pas être chargée : " + detail +
            ". Action opérateur : corrigez la table dans la console (Paramétrage › TVA), puis " +
            "faites-la revalider par l'expert-comptable avant tout envoi.";
    }
}
