namespace Liakont.Agent.Adapters.EncheresV6;

using System.Data;

/// <summary>
/// Fabrique de connexions à la base source EncheresV6, derrière l'abstraction ADO.NET
/// <see cref="IDbConnection"/>. Cette indirection permet au <see cref="PervasiveExtractor"/> d'être
/// testé unitairement avec un lecteur de données mocké/espionné (acceptance ADP02 : prouver la lecture
/// seule par les tests, pas par une simple déclaration), sans pilote Pervasive sur la machine de dev.
/// L'implémentation réelle est <see cref="OdbcEncheresV6ConnectionFactory"/> (ODBC, net48, x86 pour les
/// pilotes Pervasive 32 bits).
/// </summary>
public interface IEncheresV6ConnectionFactory
{
    /// <summary>
    /// Crée une connexion NON ouverte vers la source. L'appelant (l'extracteur) l'ouvre et la libère
    /// (<c>using</c>) — c'est lui qui traduit les défaillances en erreurs typées du contrat (R7).
    /// </summary>
    /// <returns>Une connexion ADO.NET non ouverte.</returns>
    IDbConnection CreateConnection();
}
