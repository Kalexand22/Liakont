namespace Liakont.Modules.Validation.Domain.Detection;

/// <summary>
/// Résultat de la détection d'acheteur professionnel (garde-fou B2B/B2C, F07-F08 §A.4). Détaille
/// quels indices se sont déclenchés, pour le détail technique journalisé de l'anomalie
/// <c>BUYER_LOOKS_PROFESSIONAL</c>. Le montant n'apparaît jamais : il n'est pas un critère (F07-F08 §A.4).
/// </summary>
public sealed class CompanyHintResult
{
    /// <summary>Crée un résultat de détection.</summary>
    /// <param name="hasCompanyHintField">Indice FORT : la source porte un indice « société » brut (champ <c>societe</c> renseigné).</param>
    /// <param name="hasVatNumber">Indice FORT : un n° de TVA intracommunautaire est présent sur l'acheteur.</param>
    /// <param name="matchedLegalForm">Indice MOYEN : forme juridique détectée dans la raison sociale (<c>null</c> si aucune).</param>
    public CompanyHintResult(bool hasCompanyHintField, bool hasVatNumber, string? matchedLegalForm)
    {
        HasCompanyHintField = hasCompanyHintField;
        HasVatNumber = hasVatNumber;
        MatchedLegalForm = matchedLegalForm;
    }

    /// <summary>Indice FORT : indice « société » brut porté par la source (champ <c>societe</c>, F07-F08 §A.4).</summary>
    public bool HasCompanyHintField { get; }

    /// <summary>Indice FORT : n° de TVA intracommunautaire présent sur l'acheteur (F07-F08 §A.4).</summary>
    public bool HasVatNumber { get; }

    /// <summary>Indice MOYEN : forme juridique repérée dans la raison sociale (<c>null</c> si aucune).</summary>
    public string? MatchedLegalForm { get; }

    /// <summary>Vrai si une forme juridique a été détectée dans la raison sociale.</summary>
    public bool HasLegalForm => MatchedLegalForm is not null;

    /// <summary>
    /// L'acheteur semble être un professionnel : au moins un indice FORT, OU l'indice de forme
    /// juridique (F07-F08 §A.4 — la spec ne définit qu'un seul indice moyen, jamais combiné en seuil).
    /// </summary>
    public bool LooksProfessional => HasCompanyHintField || HasVatNumber || HasLegalForm;
}
