namespace Liakont.Agent.Cli.Commands;

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Liakont.Agent.Contracts;

/// <summary>
/// Commande <c>version</c> (F12 §2.1) : affiche la version de l'agent (assembly) et la version du
/// contrat d'ingestion porté par <see cref="AgentContractVersion"/>. Utile au heartbeat, aux journaux
/// et au diagnostic de compatibilité plateforme (négociation 426).
/// </summary>
internal sealed class VersionCommand : ICliCommand
{
    public string Name => "version";

    public string Description => "Affiche la version de l'agent et du contrat d'ingestion.";

    public int Execute(IReadOnlyList<string> args, TextWriter output)
    {
        string agentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "inconnue";
        output.WriteLine($"Agent Liakont — version {agentVersion}");
        output.WriteLine($"Contrat d'ingestion — version {AgentContractVersion.ContractVersion} ({AgentContractVersion.Current})");
        return CliExitCode.Ok;
    }
}
