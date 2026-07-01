namespace Liakont.Agent.Contracts.Ged;

/// <summary>
/// Version du contrat d'ingestion GÉNÉRIQUE (canal GED, F19 §4.3.3). Le canal GED versionne SON PROPRE
/// contrat (<c>Liakont.Agent.Contracts.Ged</c>), STRICTEMENT distinct de la version du contrat pivot fiscal
/// (<c>AgentContractVersion</c>) : les deux évoluent indépendamment. Cette valeur est écrite dans la colonne
/// <c>contract_version</c> du registre <c>ged_ingestion.ged_received_documents</c> (GED05b) — jamais une
/// version fiscale. Une seule version existe en V1.
/// </summary>
public static class GedContractVersion
{
    /// <summary>Version courante du contrat d'ingestion GED (chaîne persistée au registre).</summary>
    public const string Current = "1";
}
