namespace Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Informations de compte d'une PA : consommation et limites de transactions (F05 §2). Champs
/// absents = <c>null</c> (jamais une valeur par défaut qui masquerait une donnée manquante,
/// module-rules §9).
/// </summary>
public sealed record PaAccountInfo
{
    /// <summary>Identifiant du compte côté PA.</summary>
    public required string AccountId { get; init; }

    /// <summary>Nombre de transactions consommées, ou <c>null</c> si non exposé.</summary>
    public int? TransactionsCount { get; init; }

    /// <summary>Limite de transactions, ou <c>null</c> si non exposée / illimitée.</summary>
    public int? TransactionsLimit { get; init; }

    /// <summary>Réponse brute de la PA, conservée pour l'audit (peut être <c>null</c>).</summary>
    public string? RawResponse { get; init; }
}
