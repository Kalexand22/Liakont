namespace Liakont.PaClients.B2Brouter.Wire;

/// <summary>
/// Compte B2Brouter tel que LU (F05 §2 : <c>GET /accounts/{id}.json</c> — suivi de consommation
/// <c>transactions_count</c> / <c>transactions_limit</c>). DTO PROPRIÉTAIRE, <c>internal</c>.
/// Snake_case (<see cref="B2BrouterJson"/>). Les compteurs sont des ENTIERS de transactions (pas des
/// montants) — aucun <c>double</c> en jeu.
/// </summary>
internal sealed record B2BrouterAccount
{
    /// <summary>Identifiant du compte côté B2Brouter.</summary>
    public string? Id { get; init; }

    /// <summary>Nombre de transactions consommées, ou <c>null</c> si non exposé.</summary>
    public int? TransactionsCount { get; init; }

    /// <summary>Limite de transactions, ou <c>null</c> si non exposée / illimitée.</summary>
    public int? TransactionsLimit { get; init; }
}
