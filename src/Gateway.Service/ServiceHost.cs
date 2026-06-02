namespace Conformat.Gateway.Service
{
    /// <summary>
    /// Hôte de la passerelle (composition root). Assemblera le Service Windows, l'API HTTP et le
    /// PipelineRunner aux lots SVC/API ; il est l'unique écrivain du Tracking (CLAUDE.md règle 9).
    /// Au stade du socle, ce n'est qu'un point d'entrée vide sans aucune logique métier.
    /// </summary>
    public static class ServiceHost
    {
        public static string Name => "Gateway.Service";

        /// <summary>
        /// Point d'entrée placeholder. L'hôte réel (ServiceBase + démarrage de l'API + ordonnanceur)
        /// est implémenté au lot SVC. Retourne 0 (succès) sans rien faire.
        /// </summary>
        public static int Main() => 0;
    }
}
