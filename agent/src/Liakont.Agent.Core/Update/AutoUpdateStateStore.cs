namespace Liakont.Agent.Core.Update;

using System;
using System.IO;
using Newtonsoft.Json;

/// <summary>
/// Persiste le statut de la DERNIÈRE tentative d'auto-update dans un petit fichier JSON local
/// (<see cref="AutoUpdateStatus"/>). Écrit par l'agent (refus / différé / lancement) ET par l'updater
/// détaché (résultat final). Relu par le heartbeat pour le signalement (F12 §2.5). Best-effort : ni la
/// lecture ni l'écriture ne lèvent — un fichier absent/corrompu = « pas de statut » (<c>null</c>).
/// </summary>
public sealed class AutoUpdateStateStore
{
    private readonly string _statusFilePath;

    /// <summary>Crée un store de statut sur le fichier <paramref name="statusFilePath"/>.</summary>
    /// <param name="statusFilePath">Chemin du fichier de statut JSON.</param>
    public AutoUpdateStateStore(string statusFilePath)
    {
        if (string.IsNullOrWhiteSpace(statusFilePath))
        {
            throw new ArgumentException("Le chemin du fichier de statut est requis.", nameof(statusFilePath));
        }

        _statusFilePath = statusFilePath;
    }

    /// <summary>Enregistre (écrase) le statut de la dernière tentative. Ne lève jamais.</summary>
    /// <param name="status">Le statut à mémoriser.</param>
    public void Record(AutoUpdateStatus status)
    {
        if (status == null)
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(_statusFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var dto = new StatusDto
            {
                TargetVersion = status.TargetVersion,
                Phase = status.Phase,
                Succeeded = status.Succeeded,
                Message = status.Message,
                AtUtc = DateTime.SpecifyKind(status.AtUtc, DateTimeKind.Utc),
            };
            string json = JsonConvert.SerializeObject(dto, Formatting.Indented);
            File.WriteAllText(_statusFilePath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (IOException)
        {
            // Best-effort : l'incapacité à écrire le statut ne doit pas casser l'agent.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Lit le dernier statut connu, ou <c>null</c> si absent/illisible. Ne lève jamais.</summary>
    /// <returns>Le dernier statut, ou <c>null</c>.</returns>
    public AutoUpdateStatus? TryGetLatest()
    {
        try
        {
            if (!File.Exists(_statusFilePath))
            {
                return null;
            }

            string json = File.ReadAllText(_statusFilePath);
            StatusDto? dto = JsonConvert.DeserializeObject<StatusDto>(json);
            if (dto == null || string.IsNullOrWhiteSpace(dto.Phase))
            {
                return null;
            }

            return new AutoUpdateStatus(
                dto.TargetVersion,
                dto.Phase!,
                dto.Succeeded,
                dto.Message ?? string.Empty,
                DateTime.SpecifyKind(dto.AtUtc, DateTimeKind.Utc));
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // DTO de (dé)sérialisation. Les noms de propriété JSON sont le CONTRAT partagé avec l'updater
    // autonome (qui écrit le même fichier sans référencer ce type) — ne pas les renommer en silence.
    private sealed class StatusDto
    {
        [JsonProperty("targetVersion")]
        public string? TargetVersion { get; set; }

        [JsonProperty("phase")]
        public string? Phase { get; set; }

        [JsonProperty("succeeded")]
        public bool Succeeded { get; set; }

        [JsonProperty("message")]
        public string? Message { get; set; }

        [JsonProperty("atUtc")]
        public DateTime AtUtc { get; set; }
    }
}
