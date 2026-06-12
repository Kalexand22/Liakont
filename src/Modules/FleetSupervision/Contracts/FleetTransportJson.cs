namespace Liakont.Modules.FleetSupervision.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Options JSON partagées du transport du heartbeat de flotte (OPS04). L'émetteur (publisher) et le récepteur
/// (endpoint central) DOIVENT utiliser les mêmes options : nommage web (camelCase) et énumérations en CHAÎNE
/// (lisibles + robustes à un renumérotage).
/// </summary>
public static class FleetTransportJson
{
    /// <summary>Options de (dé)sérialisation du heartbeat de flotte.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
