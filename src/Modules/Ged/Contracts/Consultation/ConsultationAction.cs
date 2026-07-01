namespace Liakont.Modules.Ged.Contracts.Consultation;

/// <summary>
/// Nature de l'opération de CONSULTATION (lecture) tracée dans le journal <c>ged_index.consultation_log</c>
/// (F19 §6.6, ADR-0036 §5). L'ensemble des valeurs est FERMÉ : la table porte un <c>CHECK</c> sur les cinq
/// chaînes correspondantes (<c>search</c>, <c>view_document</c>, <c>explore_entity</c>, <c>export</c>,
/// <c>open_archive</c>) et le writer refuse toute valeur hors énum (jamais deviner, règle 2).
/// </summary>
public enum ConsultationAction
{
    /// <summary>Recherche multidimensionnelle (<c>query_text</c> masqué selon §6.5, <c>result_count</c>, facettes).</summary>
    Search,

    /// <summary>Ouverture d'une fiche document (<c>managed_document_id</c>).</summary>
    ViewDocument,

    /// <summary>Traversée de graphe / exploration d'un objet (<c>entity_id</c>).</summary>
    ExploreEntity,

    /// <summary>Export / extraction (exige <c>liakont.ged.export</c> ; valeurs confidentielles toujours masquées).</summary>
    Export,

    /// <summary>
    /// Ouverture / attestation d'un paquet du coffre WORM par le « lien coffre » (le lien OUVRE/ATTESTE, ne
    /// modifie JAMAIS le paquet ni la chaîne fiscale <c>archive_entries</c> — WORM-neutralité, ADR-0035).
    /// </summary>
    OpenArchive,
}
