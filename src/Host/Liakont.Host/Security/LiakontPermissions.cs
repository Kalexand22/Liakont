namespace Liakont.Host.Security;

/// <summary>
/// Catalogue des permissions applicatives propres à Liakont, découvertes par
/// <c>ReflectionPermissionCatalog</c> (classe statique = abstract + sealed, nom en
/// « Permissions », champs <c>public const string</c>). Les rôles correspondants vivent
/// dans le realm Keycloak ; la matrice permission→rôle est documentée dans
/// <c>docs/architecture/identity-permissions-liakont.md</c>.
/// </summary>
public static class LiakontPermissions
{
    /// <summary>
    /// Consultation : lecture des documents, des transmissions et des journaux
    /// (aucune action mutante).
    /// </summary>
    public const string Read = "liakont.read";

    /// <summary>
    /// Actions opérateur : déblocage, relance, ré-émission et autres actions correctives
    /// sur les documents et transmissions.
    /// </summary>
    public const string Actions = "liakont.actions";

    /// <summary>
    /// Paramétrage fiscal du tenant : table TVA, mappings, comptes Plateforme Agréée,
    /// seuils et règles propres au tenant.
    /// </summary>
    public const string Settings = "liakont.settings";

    /// <summary>
    /// Supervision : vues cross-tenant en lecture seule réservées au superviseur
    /// (module Supervision).
    /// </summary>
    public const string Supervision = "liakont.supervision";

    /// <summary>
    /// Paramétrage d'INSTANCE (ADR-0039) : action MUTANTE d'instance, hors tenant (ex. configuration
    /// d'envoi d'emails de l'instance, secrets SMTP/OAuth chiffrés). Distincte de <see cref="Supervision"/>,
    /// documentée « lecture seule, aucune action mutante » : une écriture s'y contredirait. Accordée à
    /// l'opérateur d'instance (rôle <c>superviseur</c>).
    /// </summary>
    public const string InstanceSettings = "liakont.instance.settings";

    /// <summary>
    /// Méta-supervision de flotte (OPS04) : dashboard cross-INSTANCE réservé à IT Innovations
    /// (état des instances, versions, alertes). Distincte de <see cref="Supervision"/>, qui est
    /// cross-tenant DANS une instance ; la flotte est le niveau au-dessus (cross-instance).
    /// </summary>
    public const string Fleet = "liakont.fleet";
}
