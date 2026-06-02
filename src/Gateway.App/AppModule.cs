namespace Conformat.Gateway.App
{
    /// <summary>
    /// Marqueur de la console d'administration WPF. Les écrans réels (supervision, envoi manuel,
    /// audit, paramétrage comptable) arrivent au lot WPF, câblés sur l'API via <c>Gateway.ApiClient</c>.
    /// La console ne référence jamais le Core ni les plug-ins (CLAUDE.md règle 6).
    /// </summary>
    public static class AppModule
    {
        public static string Name => "Gateway.App";
    }
}
