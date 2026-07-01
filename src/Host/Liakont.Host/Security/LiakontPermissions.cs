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
    /// Méta-supervision de flotte (OPS04) : dashboard cross-INSTANCE réservé à IT Innovations
    /// (état des instances, versions, alertes). Distincte de <see cref="Supervision"/>, qui est
    /// cross-tenant DANS une instance ; la flotte est le niveau au-dessus (cross-instance).
    /// </summary>
    public const string Fleet = "liakont.fleet";

    /// <summary>
    /// GED — consultation : recherche multidimensionnelle, fiche document, exploration de graphe
    /// (F19 §6.5, ADR-0035). Capacité de <b>lecture</b> de la GED, distincte de la consultation
    /// fiscale <see cref="Read"/>. N'ouvre PAS l'accès aux axes/entités confidentiels
    /// (voir <see cref="GedConfidential"/>).
    /// </summary>
    public const string GedRead = "liakont.ged.read";

    /// <summary>
    /// GED — export : extraction / réversibilité des documents et de l'index GED (action
    /// <c>action='export'</c> journalisée, ADR-0036 §4). Gardée SÉPARÉMENT de
    /// <see cref="GedRead"/> : consulter n'autorise pas à exporter. L'export masque / exclut
    /// toujours les valeurs confidentielles (ADR-0035 INV-GED-10).
    /// </summary>
    public const string GedExport = "liakont.ged.export";

    /// <summary>
    /// GED — accès aux axes et entités marqués confidentiels (<c>is_confidential</c>). Sans cette
    /// permission, le masquage server-side (§6.5, ADR-0035 INV-GED-10) exclut ces axes / entités de
    /// TOUS les canaux de restitution (recherche, facette, graphe, export, log). Permission la plus
    /// sensible de la GED, distincte de <see cref="GedRead"/>.
    /// </summary>
    public const string GedConfidential = "liakont.ged.confidential";
}
