namespace Conformat.Gateway.PaClients.Fake
{
    /// <summary>
    /// Marqueur du plug-in PA factice (#0) — utilisé pour la démo et la suite de tests de contrat.
    /// Implémentera <c>IPaClient</c> avec ses <c>PaCapabilities</c> au lot PAA. Référence uniquement
    /// le Core (CLAUDE.md règle 6). Aucune logique au stade du socle.
    /// </summary>
    public static class FakePaClientModule
    {
        public static string Name => "Gateway.PaClients.Fake";
    }
}
