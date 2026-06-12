namespace Liakont.Agent.Installer.Deployment;

using Liakont.Agent.Cli.Diagnostics;
using Liakont.Agent.Installer.Configuration;

/// <summary>
/// Implémentation de production de <see cref="IPlatformConnectionProbe"/> : délègue au heartbeat à blanc
/// d'AGT05 (<see cref="HttpPlatformProbe"/>, commande test-api). Aucune logique dupliquée (F13 §3) : ce
/// type ne fait que traduire le diagnostic de la sonde (injoignable / clé invalide / clé révoquée / OK)
/// en <see cref="PlatformTestResult"/>. La clé reçue est déjà en clair (saisie au wizard) ; elle n'est
/// jamais persistée par cette sonde.
/// </summary>
internal sealed class HttpPlatformConnectionProbe : IPlatformConnectionProbe
{
    /// <inheritdoc />
    public PlatformTestResult Test(string platformUrl, string apiKey)
    {
        PlatformProbeResult result = HttpPlatformProbe.Probe(platformUrl, apiKey);
        return new PlatformTestResult(result.Success, result.Message);
    }
}
