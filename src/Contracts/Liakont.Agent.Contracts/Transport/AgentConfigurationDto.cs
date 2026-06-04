namespace Liakont.Agent.Contracts.Transport;

using System;

/// <summary>
/// Configuration renvoyée à l'agent par le heartbeat et par GET /api/agent/v1/configuration
/// (F12 §3.2, PIV05). Les champs de mise à jour sont alimentés par le registre de versions publié
/// par OPS07 ; tant que ce registre est vide, le comportement par défaut est SÛR :
/// <see cref="UpdateRequired"/> = <c>false</c> et les champs d'update restent <c>null</c>
/// (décision D6 du 2026-06-03 — le manifeste de version est SIGNÉ).
/// </summary>
public sealed class AgentConfigurationDto
{
    /// <summary>Crée une configuration d'agent.</summary>
    /// <param name="extractionSchedule">Planification d'extraction du tenant (expression cron), si définie.</param>
    /// <param name="extractFromUtc">Borne basse de la période à extraire (UTC, incluse), si imposée.</param>
    /// <param name="extractToUtc">Borne haute de la période à extraire (UTC, exclue), si imposée.</param>
    /// <param name="latestAgentVersion">Dernière version d'agent publiée (registre OPS07), si connue.</param>
    /// <param name="updateRequired">Mise à jour obligatoire ? Défaut <c>false</c> (sûr) tant que le registre est vide.</param>
    /// <param name="updateUrl">URL de téléchargement du paquet de mise à jour (opaque, transmise telle quelle), si applicable.</param>
    /// <param name="versionManifestSignature">Signature du manifeste de version (décision D6), si applicable.</param>
    public AgentConfigurationDto(
        string? extractionSchedule = null,
        DateTime? extractFromUtc = null,
        DateTime? extractToUtc = null,
        string? latestAgentVersion = null,
        bool updateRequired = false,
        string? updateUrl = null,
        string? versionManifestSignature = null)
    {
        ExtractionSchedule = extractionSchedule;
        ExtractFromUtc = extractFromUtc;
        ExtractToUtc = extractToUtc;
        LatestAgentVersion = latestAgentVersion;
        UpdateRequired = updateRequired;
        UpdateUrl = updateUrl;
        VersionManifestSignature = versionManifestSignature;
    }

    /// <summary>Planification d'extraction du tenant (expression cron).</summary>
    public string? ExtractionSchedule { get; }

    /// <summary>Borne basse de la période à extraire (UTC, incluse).</summary>
    public DateTime? ExtractFromUtc { get; }

    /// <summary>Borne haute de la période à extraire (UTC, exclue).</summary>
    public DateTime? ExtractToUtc { get; }

    /// <summary>Dernière version d'agent publiée (registre OPS07).</summary>
    public string? LatestAgentVersion { get; }

    /// <summary>Mise à jour obligatoire ? Défaut <c>false</c> (comportement sûr).</summary>
    public bool UpdateRequired { get; }

    /// <summary>URL de téléchargement du paquet de mise à jour (opaque), si applicable.</summary>
    public string? UpdateUrl { get; }

    /// <summary>Signature du manifeste de version (décision D6).</summary>
    public string? VersionManifestSignature { get; }
}
