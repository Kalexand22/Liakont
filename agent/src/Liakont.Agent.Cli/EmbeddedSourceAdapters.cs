namespace Liakont.Agent.Cli;

using System.Collections.Generic;
using System.Linq;
using Liakont.Agent.Adapters.EncheresV6;
using Liakont.Agent.Core;

/// <summary>
/// Registre UNIQUE des adaptateurs source EMBARQUÉS dans cette version de l'agent (les plug-ins
/// IExtractor réellement livrés). Source de vérité partagée par le CLI (« adaptateur connu » de
/// check-config) et l'installeur (menu source du wizard + check-config), pour éviter deux listes
/// parallèles qui dériveraient (CLAUDE.md n°6). Un de plus à chaque item ADP.
/// </summary>
internal static class EmbeddedSourceAdapters
{
    /// <summary>Instancie les adaptateurs source embarqués.</summary>
    public static IReadOnlyList<IExtractor> Create() => new IExtractor[]
    {
        new EncheresV6Extractor(),
    };

    /// <summary>Noms (SourceName) des adaptateurs source embarqués.</summary>
    public static string[] Names() => Create().Select(a => a.SourceName).ToArray();
}
