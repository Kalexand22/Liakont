namespace Liakont.Host.FleetApi;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Validation de la clé d'ingestion du heartbeat de flotte (OPS04). Comparaison à TEMPS CONSTANT
/// (<see cref="CryptographicOperations.FixedTimeEquals"/>) pour ne pas fuiter la clé par mesure de timing.
/// Une clé configurée vide ou une clé présentée vide refuse l'accès (le central doit avoir une clé).
/// </summary>
internal static class FleetApiKeyValidator
{
    public static bool IsAuthorized(string? configuredKey, string? providedKey)
    {
        if (string.IsNullOrEmpty(configuredKey) || string.IsNullOrEmpty(providedKey))
        {
            return false;
        }

        byte[] expected = Encoding.UTF8.GetBytes(configuredKey);
        byte[] actual = Encoding.UTF8.GetBytes(providedKey);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
