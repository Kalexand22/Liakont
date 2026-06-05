namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using System.Data;
using System.Data.Odbc;

/// <summary>
/// Fabrique de connexions ODBC réelles vers la base EncheresV6 (Magic XPA / Pervasive / Zen), net48.
/// Les pilotes ODBC Pervasive étant souvent 32 bits, l'adaptateur se compile aussi en x86
/// (Directory.Build.props). La chaîne de connexion est un PARAMÉTRAGE de tenant (chiffrée DPAPI,
/// fournie par la configuration de l'agent — ADP04) : aucune donnée client n'est embarquée ici
/// (CLAUDE.md n°7).
/// <para>
/// LECTURE SEULE (CLAUDE.md n°5) : la garantie produit vient du <see cref="PervasiveExtractor"/>
/// (requêtes <c>SELECT</c> uniquement, garde <see cref="Source.EncheresV6Schema.EnsureSelectOnly"/>,
/// aucune transaction d'écriture, aucun verrou). Si le pilote configuré expose un attribut « read-only »,
/// il est ajouté à la chaîne par la configuration (défense en profondeur dépendante du pilote) — cette
/// fabrique n'invente aucun attribut de pilote et transmet la chaîne telle quelle.
/// </para>
/// </summary>
public sealed class OdbcEncheresV6ConnectionFactory : IEncheresV6ConnectionFactory
{
    private readonly string _connectionString;

    /// <summary>Crée la fabrique pour une chaîne de connexion ODBC déjà déchiffrée.</summary>
    /// <param name="connectionString">Chaîne de connexion ODBC (paramétrage tenant, jamais en clair dans le code).</param>
    /// <exception cref="ArgumentException">Si la chaîne est vide.</exception>
    public OdbcEncheresV6ConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "La chaîne de connexion ODBC EncheresV6 est requise (paramétrage tenant — ADP04).",
                nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public IDbConnection CreateConnection() => new OdbcConnection(_connectionString);
}
