namespace Liakont.Agent.Installer.Configuration;

/// <summary>
/// Port du test de connexion à la base source (écran source, F13 §4.1). L'implémentation de production
/// délègue à la sonde ODBC en LECTURE SEULE d'AGT05 (CLAUDE.md n°5 : aucune écriture, aucun verrou) ;
/// les tests injectent une doublure (« Core mocké »). Le moteur d'installation ne connaît que ce port,
/// jamais la sonde concrète — d'où sa testabilité hors machine réelle.
/// </summary>
internal interface ISourceConnectionProbe
{
    /// <summary>Teste la chaîne ODBC fournie (déchiffrée). Ne lève jamais d'exception : tout échec devient un message.</summary>
    SourceTestResult Test(string odbcConnectionString);
}
