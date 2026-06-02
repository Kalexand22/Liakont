namespace Conformat.Gateway.PaClients.B2Brouter
{
    /// <summary>
    /// Marqueur du plug-in PA B2Brouter (eDocExchange). Implémentera <c>IPaClient</c> et déclarera
    /// ses <c>PaCapabilities</c> au lot PAB. Référence uniquement le Core (CLAUDE.md règle 6).
    /// Aucune logique au stade du socle.
    /// </summary>
    public static class B2BrouterModule
    {
        public static string Name => "Gateway.PaClients.B2Brouter";
    }
}
