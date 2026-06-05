namespace Liakont.Agent.Cli;

using System.Collections.Generic;
using System.IO;

/// <summary>
/// Une commande du CLI de diagnostic de l'agent (F12 §2.1). Chaque commande est isolée et reçoit
/// ses dépendances par injection (les sondes ODBC/API, le protecteur de secrets, la file locale),
/// ce qui permet de la tester avec des doublures (acceptation AGT05 « commandes mockées »).
/// </summary>
internal interface ICliCommand
{
    /// <summary>Nom invoqué en ligne de commande (ex. <c>check-config</c>).</summary>
    string Name { get; }

    /// <summary>Résumé d'une ligne affiché dans l'aide.</summary>
    string Description { get; }

    /// <summary>
    /// Exécute la commande avec ses arguments propres (sans le nom de la commande) et écrit sa
    /// sortie sur <paramref name="output"/>. Renvoie un <see cref="CliExitCode"/>.
    /// </summary>
    int Execute(IReadOnlyList<string> args, TextWriter output);
}
