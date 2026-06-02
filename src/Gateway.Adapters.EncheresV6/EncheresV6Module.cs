namespace Conformat.Gateway.Adapters.EncheresV6
{
    /// <summary>
    /// Marqueur du plug-in source EncheresV6. Fournira <c>PervasiveExtractor</c> (ODBC réel,
    /// lecture seule stricte), <c>EncheresV6FixtureExtractor</c> (rejeu JSON) et
    /// <c>EncheresV6RowMapper</c> au lot ADP. N'implémente que <c>IExtractor</c> ; référence
    /// uniquement le Core (CLAUDE.md règles 5 et 6). Aucune logique au stade du socle.
    /// </summary>
    public static class EncheresV6Module
    {
        public static string Name => "Gateway.Adapters.EncheresV6";
    }
}
