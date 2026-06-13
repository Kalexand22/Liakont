namespace Liakont.Agent.Installer.Tests.Fakes;

using System;
using Liakont.Agent.Installer.Configuration;

/// <summary>
/// Doublure de <see cref="IPlatformConnectionProbe"/> : renvoie un résultat fixe et enregistre la dernière
/// URL et la dernière clé testées (pour asserter la délégation du moteur).
/// </summary>
internal sealed class FakePlatformProbe : IPlatformConnectionProbe
{
    private readonly PlatformTestResult _result;

    public FakePlatformProbe(PlatformTestResult result)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public string? LastUrl { get; private set; }

    public string? LastApiKey { get; private set; }

    public PlatformTestResult Test(string platformUrl, string apiKey)
    {
        LastUrl = platformUrl;
        LastApiKey = apiKey;
        return _result;
    }
}
