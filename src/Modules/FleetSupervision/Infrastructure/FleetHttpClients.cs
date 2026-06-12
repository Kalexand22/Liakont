namespace Liakont.Modules.FleetSupervision.Infrastructure;

/// <summary>Noms des clients HTTP nommés de la méta-supervision de flotte (OPS04).</summary>
internal static class FleetHttpClients
{
    /// <summary>Client d'envoi du heartbeat d'instance vers le central.</summary>
    public const string Reporting = "FleetReporting";

    /// <summary>Client de la sonde Keycloak (santé de l'IdP).</summary>
    public const string KeycloakProbe = "FleetKeycloakProbe";
}
