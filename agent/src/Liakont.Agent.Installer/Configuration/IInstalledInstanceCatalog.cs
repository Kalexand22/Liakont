namespace Liakont.Agent.Installer.Configuration;

using System.Collections.Generic;

/// <summary>
/// Port d'énumération des instances de l'agent DÉJÀ installées sur le poste (multi-instances, OPS05 pt 5,
/// cas serveur SaaS éditeur). L'implémentation de production lit les services Windows « LiakontAgent$* » ;
/// les tests injectent une doublure. Sert au wizard à afficher les instances présentes et à refuser un
/// nom déjà pris.
/// </summary>
internal interface IInstalledInstanceCatalog
{
    /// <summary>
    /// Noms (canoniques, p. ex. « Default », « ClientA ») des instances installées sur ce poste. Liste
    /// vide si aucune. Une entrée non reconnaissable comme instance Liakont est ignorée.
    /// </summary>
    IReadOnlyList<string> ListInstalledInstanceNames();
}
