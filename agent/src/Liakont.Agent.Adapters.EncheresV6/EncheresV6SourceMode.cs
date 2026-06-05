namespace Liakont.Agent.Adapters.EncheresV6;

/// <summary>
/// Mode de la source EncheresV6, choisi par CONFIGURATION (jamais par compilation — ADP04). Le mode
/// est dérivé sans ambiguïté de <see cref="EncheresV6AdapterConfig"/> : une chaîne ODBC sélectionne le
/// mode <see cref="Pervasive"/>, un chemin de fixtures le mode <see cref="Fixture"/>. Les deux à la fois
/// ou aucun des deux sont refusés à la validation (« chaîne ODBC OU chemin fixtures requis »).
/// </summary>
public enum EncheresV6SourceMode
{
    /// <summary>Extraction ODBC réelle (Magic XPA / Pervasive / Zen) via <see cref="PervasiveExtractor"/>.</summary>
    Pervasive,

    /// <summary>Rejeu de fixtures JSON via <see cref="EncheresV6FixtureExtractor"/> (dev sans licence, démo, tests).</summary>
    Fixture,
}
