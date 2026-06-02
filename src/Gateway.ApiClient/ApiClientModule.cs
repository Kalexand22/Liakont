namespace Conformat.Gateway.ApiClient
{
    /// <summary>
    /// Marqueur du client .NET de l'API HTTP. Encapsulera les appels HTTP (TLS 1.2/1.3) vers le
    /// Service en exposant les DTOs de <c>Gateway.Api</c>, aux lots API. Aucune logique au stade du socle.
    /// </summary>
    public static class ApiClientModule
    {
        public static string Name => "Gateway.ApiClient";
    }
}
