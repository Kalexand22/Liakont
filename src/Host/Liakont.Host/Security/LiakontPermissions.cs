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
}
