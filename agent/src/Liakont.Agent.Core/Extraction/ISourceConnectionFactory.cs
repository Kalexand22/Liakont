namespace Liakont.Agent.Core.Extraction;

using System.Data;

/// <summary>
/// Fabrique de connexions à une base source (abstraction testable — un adaptateur peut être éprouvé
/// avec une connexion doublée, sans pilote ODBC réel). L'implémentation de production
/// (<see cref="OdbcSourceConnectionFactory"/>) ouvre une connexion ODBC en LECTURE SEULE (CLAUDE.md n°5) :
/// la connexion n'est jamais utilisée pour écrire, verrouiller ou ouvrir une transaction d'écriture.
/// </summary>
public interface ISourceConnectionFactory
{
    /// <summary>Crée une connexion NON ouverte vers la base source (l'appelant l'ouvre et la libère).</summary>
    /// <returns>Une connexion <see cref="IDbConnection"/> prête à être ouverte.</returns>
    IDbConnection CreateConnection();
}
