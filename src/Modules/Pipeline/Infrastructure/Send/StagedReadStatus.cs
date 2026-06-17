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
}
