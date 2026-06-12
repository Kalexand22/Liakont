namespace Liakont.Agent.Installer.Tests.Fakes;

using System.Collections.Generic;
using Liakont.Agent.Installer.Configuration;

/// <summary>
/// Doublure de <see cref="IInstalledInstanceCatalog"/> : renvoie une liste fixe d'instances « déjà
/// installées » (pour tester la détection et le refus d'un nom déjà pris).
/// </summary>
internal sealed class FakeInstanceCatalog : IInstalledInstanceCatalog
{
    private readonly IReadOnlyList<string> _installed;

    public FakeInstanceCatalog(params string[] installed)
    {
        _installed = installed;
    }

    public IReadOnlyList<string> ListInstalledInstanceNames() => _installed;
}
