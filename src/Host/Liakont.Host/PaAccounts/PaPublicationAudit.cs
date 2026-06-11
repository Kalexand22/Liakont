namespace Liakont.Host.PaAccounts;

/// <summary>
/// Source UNIQUE des codes d'audit de la publication du SIREN / tax_report_setting (FIX201). Tracer cette
/// opération est exigé par la décision E1 (« trace l'opération ») et la piste d'audit append-only
/// (CLAUDE.md n°4). Aucun secret n'est jamais journalisé (il n'y en a pas dans cette opération).
/// </summary>
internal static class PaPublicationAudit
{
    /// <summary>Type d'entité auditée : le compte Plateforme Agréée dont le réglage est publié.</summary>
    public const string EntityType = "PaAccount";

    /// <summary>Type d'activité : publication du SIREN / activation de la transmission auprès de la PA.</summary>
    public const string PublishedActivity = "tax_report_setting_published";
}
