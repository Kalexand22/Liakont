namespace Liakont.Agent.Core.Extraction;

using System;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Agent.Core.Configuration;

/// <summary>
/// Configuration commune « identité de l'émetteur + nature d'opération » d'un adaptateur source, lue
/// dans la section <c>adapterConfig.&lt;nom&gt;</c> de <c>agent.json</c> (ADR-0023). Le SIREN et la raison
/// sociale de l'émetteur, ainsi que la nature d'opération, sont du PARAMÉTRAGE TENANT : ils sont ABSENTS
/// de la base source (F01-F02 §4.3) et ne sont JAMAIS devinés (CLAUDE.md n°2). Partagée par les
/// adaptateurs dont l'émetteur vient de la config (<see cref="EmitterIdentitySource.FromConfig"/>). Un
/// champ obligatoire manquant ou une nature d'opération inconnue lève une <see cref="AgentConfigException"/>
/// (message français nommant le paramètre) — bloquer plutôt qu'extraire avec une identité fausse (n°3).
/// </summary>
public sealed class SourceEmitterConfig
{
    /// <summary>Clé du SIREN émetteur dans <c>adapterConfig</c>.</summary>
    public const string EmitterSirenKey = "emitterSiren";

    /// <summary>Clé de la raison sociale émettrice dans <c>adapterConfig</c>.</summary>
    public const string EmitterNameKey = "emitterName";

    /// <summary>Clé de la nature d'opération dans <c>adapterConfig</c>.</summary>
    public const string OperationCategoryKey = "operationCategory";

    /// <summary>Crée la configuration d'émetteur.</summary>
    /// <param name="emitterSiren">SIREN de l'émetteur (paramétrage tenant).</param>
    /// <param name="emitterName">Raison sociale de l'émetteur.</param>
    /// <param name="operationCategory">Nature d'opération.</param>
    public SourceEmitterConfig(string emitterSiren, string emitterName, OperationCategory operationCategory)
    {
        EmitterSiren = emitterSiren ?? throw new ArgumentNullException(nameof(emitterSiren));
        EmitterName = emitterName ?? throw new ArgumentNullException(nameof(emitterName));
        OperationCategory = operationCategory;
    }

    /// <summary>SIREN de l'émetteur (EN 16931 BT-30, scheme 0002).</summary>
    public string EmitterSiren { get; }

    /// <summary>Raison sociale de l'émetteur (EN 16931 BT-27).</summary>
    public string EmitterName { get; }

    /// <summary>Nature d'opération (F01-F02 §3.1) — conditionne l'e-reporting de paiement.</summary>
    public OperationCategory OperationCategory { get; }

    /// <summary>
    /// Lit et valide la section <c>adapterConfig.&lt;nom&gt;</c>. Lève <see cref="AgentConfigException"/>
    /// (français, nommant le paramètre) si un champ obligatoire manque ou si la nature d'opération est inconnue.
    /// </summary>
    /// <param name="section">La section de configuration de l'adaptateur (jamais <c>null</c>).</param>
    /// <returns>La configuration validée.</returns>
    public static SourceEmitterConfig FromSection(AdapterConfigSection section)
    {
        if (section is null)
        {
            throw new ArgumentNullException(nameof(section));
        }

        string siren = section.GetRequired(EmitterSirenKey);
        string name = section.GetRequired(EmitterNameKey);
        string operationRaw = section.GetRequired(OperationCategoryKey);

        if (!Enum.TryParse(operationRaw, ignoreCase: true, out OperationCategory operation)
            || !Enum.IsDefined(typeof(OperationCategory), operation))
        {
            throw new AgentConfigException(
                $"Le paramètre « adapterConfig.{section.AdapterName}.{OperationCategoryKey} » (« {operationRaw} ») "
                + $"est invalide. Valeurs admises : {string.Join(", ", Enum.GetNames(typeof(OperationCategory)))}.");
        }

        return new SourceEmitterConfig(siren, name, operation);
    }
}
