namespace Liakont.Modules.TvaMapping.Infrastructure.Handlers.Commands;

/// <summary>Messages opérateur (français) partagés par les handlers d'édition de la table de mapping TVA.</summary>
internal static class MappingEditMessages
{
    /// <summary>Aucune table de mapping n'est paramétrée pour le tenant courant (édition impossible).</summary>
    public const string NoTableForTenant =
        "Aucune table de mapping TVA n'est paramétrée pour cette société. " +
        "Action opérateur : importez ou créez la table dans la console (Paramétrage › TVA) avant de l'éditer.";
}
