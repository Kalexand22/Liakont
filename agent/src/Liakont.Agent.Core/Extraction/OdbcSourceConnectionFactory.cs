namespace Liakont.Agent.Core.Extraction;

using System;
using System.Data;
using System.Data.Odbc;

/// <summary>
/// Fabrique de connexions ODBC en LECTURE SEULE vers une base source (CLAUDE.md n°5). La chaîne de
/// connexion (paramétrage tenant) est déchiffrée par l'appelant (DPAPI) avant d'être passée ici — elle
/// n'est jamais journalisée ni réécrite en clair (CLAUDE.md n°10). La connexion produite n'est utilisée
/// que pour des <c>SELECT</c> ; aucune écriture, aucun verrou, aucune transaction d'écriture.
/// </summary>
public sealed class OdbcSourceConnectionFactory : ISourceConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>Crée la fabrique avec la chaîne ODBC (déjà déchiffrée).</summary>
    /// <param name="connectionString">Chaîne de connexion ODBC en clair (déchiffrée DPAPI par l'appelant).</param>
    /// <exception cref="ArgumentException">Si la chaîne est nulle ou vide.</exception>
    public OdbcSourceConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("La chaîne de connexion ODBC est requise.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public IDbConnection CreateConnection() => new OdbcConnection(_connectionString);
}
