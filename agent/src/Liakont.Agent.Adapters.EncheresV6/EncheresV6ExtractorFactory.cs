namespace Liakont.Agent.Adapters.EncheresV6;

using System;
using System.IO;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core;
using Liakont.Agent.Core.Configuration;
using Liakont.Agent.Core.Security;

/// <summary>
/// Fabrique l'<see cref="IExtractor"/> EncheresV6 d'après sa configuration (ADP04). C'est ici — le
/// composition root de l'adaptateur — que le <see cref="EncheresV6SourceMode"/> tranché par la config
/// devient un extracteur concret : <see cref="PervasiveExtractor"/> (ODBC réel) ou
/// <see cref="EncheresV6FixtureExtractor"/> (fixtures). Le choix est piloté par la CONFIGURATION, jamais
/// par compilation ni par un <c>if</c> sur un type (CLAUDE.md n°8 — frontière de généricité côté agent).
/// <para>
/// La chaîne ODBC n'est déchiffrée (DPAPI) qu'ICI, au moment de bâtir la fabrique de connexions, et
/// jamais journalisée (CLAUDE.md n°10). Un secret non déchiffrable (chiffré sur une autre machine,
/// valeur en clair) lève une <see cref="AgentConfigException"/> française, sans fuiter le secret.
/// </para>
/// </summary>
public static class EncheresV6ExtractorFactory
{
    /// <summary>
    /// Crée l'extracteur correspondant au mode déclaré par <paramref name="config"/>.
    /// </summary>
    /// <param name="config">Configuration typée de l'adaptateur (mode + source).</param>
    /// <param name="protector">Protecteur de secrets (déchiffre la chaîne ODBC en mode Pervasive).</param>
    /// <param name="emitter">Identité de l'émetteur (paramétrage tenant — SIREN absent de la base).</param>
    /// <param name="operationCategory">Nature d'opération de la source (paramétrage — F01-F02 §7 #3).</param>
    /// <returns>L'extracteur configuré.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="config"/>, <paramref name="protector"/> ou <paramref name="emitter"/> est nul.</exception>
    /// <exception cref="AgentConfigException">Si la chaîne ODBC est absente/illisible, ou le chemin de fixtures manquant.</exception>
    public static IExtractor Create(
        EncheresV6AdapterConfig config,
        ISecretProtector protector,
        EncheresV6EmitterIdentity emitter,
        OperationCategory operationCategory)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (protector is null)
        {
            throw new ArgumentNullException(nameof(protector));
        }

        if (emitter is null)
        {
            throw new ArgumentNullException(nameof(emitter));
        }

        switch (config.Mode)
        {
            case EncheresV6SourceMode.Pervasive:
                return CreatePervasive(config, protector, emitter, operationCategory);

            case EncheresV6SourceMode.Fixture:
                return CreateFixture(config, emitter, operationCategory);

            default:
                // Inatteignable : EncheresV6AdapterConfig ne produit que Pervasive ou Fixture.
                throw new AgentConfigException(
                    $"Mode source EncheresV6 inconnu : « {config.Mode} ». Vérifiez la configuration de l'adaptateur.");
        }
    }

    private static PervasiveExtractor CreatePervasive(
        EncheresV6AdapterConfig config,
        ISecretProtector protector,
        EncheresV6EmitterIdentity emitter,
        OperationCategory operationCategory)
    {
        string connectionString;
        try
        {
            connectionString = protector.Unprotect(config.OdbcConnectionStringProtected!);
        }
        catch (Exception ex)
        {
            // Le message NE contient JAMAIS la valeur (chiffrée ou claire) du secret (CLAUDE.md n°10).
            throw new AgentConfigException(
                "La chaîne de connexion ODBC EncheresV6 n'est pas déchiffrable sur ce poste : "
                + ex.Message + " Re-chiffrez-la sur CETTE machine avec « liakont-agent-cli encrypt » (DPAPI est lié au poste).");
        }

        return new PervasiveExtractor(
            new OdbcEncheresV6ConnectionFactory(connectionString),
            emitter,
            operationCategory);
    }

    private static EncheresV6FixtureExtractor CreateFixture(
        EncheresV6AdapterConfig config,
        EncheresV6EmitterIdentity emitter,
        OperationCategory operationCategory)
    {
        string path = config.FixturesPath!;

        // Un répertoire fusionne tous ses *.json (plusieurs cas) ; sinon, un fichier unique. Le mode
        // fixtures lève une SourceSchemaException française si le chemin est introuvable (jamais en silence).
        return Directory.Exists(path)
            ? EncheresV6FixtureExtractor.FromDirectory(path, emitter, operationCategory)
            : EncheresV6FixtureExtractor.FromFile(path, emitter, operationCategory);
    }
}
