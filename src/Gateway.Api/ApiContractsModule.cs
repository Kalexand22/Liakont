namespace Conformat.Gateway.Api
{
    /// <summary>
    /// Marqueur des contrats de l'API HTTP. Portera les DTOs neutres (requêtes/réponses) échangés
    /// entre la console et le Service aux lots API. Bibliothèque pure, sans dépendance de projet.
    /// Aucune logique au stade du socle.
    /// </summary>
    public static class ApiContractsModule
    {
        public static string Name => "Gateway.Api";
    }
}
