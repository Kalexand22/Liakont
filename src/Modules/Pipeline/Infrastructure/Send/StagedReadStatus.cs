namespace Liakont.Modules.Pipeline.Infrastructure.Send;

/// <summary>Issue de la relecture du contenu stagé au SEND (PIP00) : présent / absent (transitoire) /
/// altéré / émetteur non résolu (profil tenant incomplet au moment de l'envoi).</summary>
internal enum StagedReadStatus
{
    /// <summary>Le pivot a été relu, enrichi (émetteur read-time) et son intégrité re-vérifiée.</summary>
    Ok,

    /// <summary>Aucune entrée de staging : transitoire (ADR-0014), à re-tenter — jamais terminal.</summary>
    NotStaged,

    /// <summary>Contenu altéré/illisible : à bloquer (« bloquer plutôt qu'envoyer faux »).</summary>
    Integrity,

    /// <summary>L'émetteur (profil tenant) n'est plus résolvable au SEND — le profil a perdu son SIREN
    /// entre le CHECK et l'envoi (RB9). On NE transmet PAS un document sans émetteur (« bloquer plutôt
    /// qu'envoyer faux », CLAUDE.md n°3) ; transitoire : repris dès que le profil est rétabli.</summary>
    EmitterUnresolved,

    /// <summary>La catégorie de TVA (UNCL5305) n'a pas pu être reposée sur le pivot au SEND — la table de
    /// mapping TVA a changé depuis le CHECK (un régime n'est plus couvert). On NE transmet PAS un document
    /// sans catégorie (la PA l'exige par ligne, EN 16931 BG-30) ; transitoire : repris dès que la table est
    /// rétablie. L'enrichissement TVA est read-time, SYMÉTRIQUE à l'émetteur (emitter-filled-by-platform).</summary>
    TvaUnresolved,
}
