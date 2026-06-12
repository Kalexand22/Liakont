namespace Liakont.Agent.Installer.Tests.Fakes;

using System;
using Liakont.Agent.Installer.Configuration;

/// <summary>
/// Doublure de <see cref="ISourceConnectionProbe"/> : renvoie un résultat fixe et enregistre la dernière
/// chaîne testée (pour asserter la délégation du moteur).
/// </summary>
internal sealed class FakeSourceProbe : ISourceConnectionProbe
{
    private readonly SourceTestResult _result;

    public FakeSourceProbe(SourceTestResult result)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public string? LastConnectionString { get; private set; }

    public SourceTestResult Test(string odbcConnectionString)
    {
        LastConnectionString = odbcConnectionString;
        return _result;
    }
}
