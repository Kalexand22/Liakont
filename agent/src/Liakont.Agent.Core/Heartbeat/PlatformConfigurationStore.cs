namespace Liakont.Agent.Core.Heartbeat;

using System;
using Liakont.Agent.Contracts.Transport;
using Liakont.Agent.Core.Storage;
using Newtonsoft.Json;

/// <summary>
/// Persiste la DERNIÈRE configuration reçue de la plateforme (heartbeat ou GET /configuration) dans
/// <c>agent_state</c> (F12 §2.3 « dernière config reçue de la plateforme »). Permet à l'agent de
/// redémarrer avec la configuration plateforme connue si celle-ci est momentanément injoignable
/// (F12 §2.5 — repli local). Une seule valeur (la plus récente écrase la précédente).
/// <para>
/// La lecture est tolérante : une valeur absente ou corrompue renvoie <c>null</c> (l'agent retombe
/// sur sa configuration locale), jamais une exception — une config stockée illisible ne doit pas
/// empêcher l'agent de démarrer.
/// </para>
/// </summary>
public sealed class PlatformConfigurationStore
{
    private readonly LocalQueue _queue;

    /// <summary>Crée un store au-dessus de la file locale.</summary>
    /// <param name="queue">File locale (porteuse de la table <c>agent_state</c>).</param>
    public PlatformConfigurationStore(LocalQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <summary>Enregistre (en écrasant) la configuration plateforme reçue.</summary>
    /// <param name="configuration">La configuration à mémoriser.</param>
    public void Save(AgentConfigurationDto configuration)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        _queue.SetState(LocalQueue.LastConfigurationKey, JsonConvert.SerializeObject(configuration));
    }

    /// <summary>Lit la dernière configuration plateforme mémorisée, ou <c>null</c> si absente/illisible.</summary>
    /// <returns>La configuration, ou <c>null</c>.</returns>
    public AgentConfigurationDto? TryGet()
    {
        string? raw = _queue.GetState(LocalQueue.LastConfigurationKey);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        try
        {
            // raw est non-null/non-vide (garde ci-dessus) ; le BCL net48 n'annote pas IsNullOrEmpty,
            // d'où le null-forgiving pour le paramètre annoté non-null de Newtonsoft.
            return JsonConvert.DeserializeObject<AgentConfigurationDto>(raw!);
        }
        catch (JsonException)
        {
            // Valeur corrompue (format de contrat changé, écriture partielle) : on l'ignore et l'agent
            // repart sur sa configuration locale plutôt que d'échouer au démarrage.
            return null;
        }
    }
}
