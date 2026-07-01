namespace Liakont.Modules.Ged.Contracts.Consultation;

/// <summary>
/// Régime de robustesse de l'écriture du journal de consultation (F19 §6.6, ADR-0036 §3). Le choix vient d'une
/// CAPACITÉ TENANT (paramétrage en base), JAMAIS d'un <c>if (tenant is ...)</c> ni d'une obligation légale produit
/// inventée. Tant que le point ouvert D8 (finalité probante, owner Sécurité + DPO) n'est pas tranché, le défaut
/// <see cref="BestEffort"/> s'applique ; le régime <see cref="Evidential"/> est GRAVÉ et prêt à activer sans
/// réécriture (le mécanisme est en place, son déclenchement est du paramétrage).
/// </summary>
public enum ConsultationAuditMode
{
    /// <summary>
    /// DÉFAUT défendable : une lecture n'a pas de transaction métier à casser. Si l'écriture de la trace échoue,
    /// la lecture RÉUSSIT et l'échec est journalisé en Warning (observabilité, message FR). Aucune valeur probante
    /// présumée (D8).
    /// </summary>
    BestEffort,

    /// <summary>
    /// Régime PROBANT (D8 confirmé par le tenant) : l'écriture de la trace est une PRÉCONDITION de l'accès —
    /// fail-closed (la lecture est refusée si la trace échoue, message FR opérateur). On ne DÉGRADE JAMAIS en
    /// silence vers un simple Warning (CLAUDE.md n.3 : « bloquer plutôt qu'affirmer une trace qui n'existe pas »).
    /// </summary>
    Evidential,
}
