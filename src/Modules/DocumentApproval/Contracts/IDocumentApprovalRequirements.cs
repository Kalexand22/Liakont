namespace Liakont.Modules.DocumentApproval.Contracts;

/// <summary>
/// Paramétrage TENANT du niveau de preuve eIDAS requis PAR purpose (ADR-0028 §5 cond. 2, F17 §7 ; INV-APPROVAL-4).
/// Le niveau exigé est un CHOIX du tenant, JAMAIS un défaut produit ni une obligation codée (CLAUDE.md n°2/3) :
/// en l'absence de configuration, le défaut est <c>Recorded</c> (acceptation enregistrée, aucune preuve externe
/// requise — un tenant en <c>Recorded</c> n'est jamais bloqué du seul fait de l'absence de fournisseur de
/// signature). La configuration est <b>mutable</b> (ce n'est pas une table d'audit) ; les niveaux sont exposés par
/// leur NOM (<c>Recorded</c>/<c>SES</c>/<c>AES</c>/<c>QES</c>) pour garder <c>Contracts</c> sans dépendance sur
/// <c>Signature.Contracts</c>. Toujours scopé par <paramref name="companyId"/> (CLAUDE.md n°9).
/// </summary>
public interface IDocumentApprovalRequirements
{
    /// <summary>
    /// Niveau de preuve requis (nom de <c>SignatureLevel</c>) configuré par le tenant pour ce <paramref name="purpose"/>.
    /// Renvoie <c>"Recorded"</c> quand aucune exigence n'a été configurée (défaut, jamais un blocage).
    /// </summary>
    Task<string> GetRequiredLevelAsync(Guid companyId, ValidationPurpose purpose, CancellationToken ct = default);

    /// <summary>
    /// Configure (upsert) le niveau de preuve requis pour ce <paramref name="purpose"/>.
    /// <paramref name="requiredLevelName"/> doit être un niveau UNIQUE et applicable (<c>Recorded</c>, <c>SES</c>,
    /// <c>AES</c> ou <c>QES</c> ; ni <c>None</c> ni un drapeau combiné) — sinon une <c>ArgumentException</c> est
    /// levée (jamais d'exigence silencieusement invalide). Le durcissement reste un choix tenant, jamais une
    /// obligation produit (CLAUDE.md n°2/3).
    /// </summary>
    Task SetRequiredLevelAsync(
        Guid companyId,
        ValidationPurpose purpose,
        string requiredLevelName,
        CancellationToken ct = default);
}
