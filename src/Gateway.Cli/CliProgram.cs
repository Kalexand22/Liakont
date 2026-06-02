namespace Conformat.Gateway.Cli
{
    /// <summary>
    /// Utilitaire en ligne de commande (mise en service, mode secours quand le Service est arrêté).
    /// Accédera directement au Core et aux plug-ins sous mutex global mono-écrivain (CLAUDE.md règle 9)
    /// aux lots CLI. Au stade du socle, ce n'est qu'un point d'entrée vide sans logique métier.
    /// </summary>
    public static class CliProgram
    {
        public static string Name => "Gateway.Cli";

        /// <summary>Point d'entrée placeholder. Les commandes réelles arrivent au lot CLI.</summary>
        public static int Main() => 0;
    }
}
