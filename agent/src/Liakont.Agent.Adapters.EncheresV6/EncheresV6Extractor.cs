namespace Liakont.Agent.Adapters.EncheresV6;

using Liakont.Agent.Core;

/// <summary>
/// Plug-in source #1 (EncheresV6 — Magic XPA / Pervasive). Placeholder : l'extraction ODBC
/// en lecture seule et le mapping vers le pivot EN 16931 arrivent avec les items ADP.
/// </summary>
public sealed class EncheresV6Extractor : IExtractor
{
    /// <inheritdoc />
    public string SourceName => "EncheresV6";
}
