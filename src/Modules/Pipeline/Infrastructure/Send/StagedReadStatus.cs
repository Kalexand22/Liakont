namespace Liakont.Modules.Pipeline.Infrastructure.Send;

/// <summary>Issue de la relecture du contenu stagé au SEND (PIP00) : présent / absent (transitoire) / altéré.</summary>
internal enum StagedReadStatus
{
    /// <summary>Le pivot a été relu et son intégrité re-vérifiée.</summary>
    Ok,

    /// <summary>Aucune entrée de staging : transitoire (ADR-0014), à re-tenter — jamais terminal.</summary>
    NotStaged,

    /// <summary>Contenu altéré/illisible : à bloquer (« bloquer plutôt qu'envoyer faux »).</summary>
    Integrity,
}
