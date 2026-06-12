namespace Liakont.Agent.Installer.Configuration;

/// <summary>
/// Port du test du serveur centralisé (écran serveur, F13 §4.2). L'implémentation de production délègue
/// au heartbeat à blanc d'AGT05 (test-api) ; les tests injectent une doublure. Aucune donnée n'est
/// poussée — seul l'état joignabilité/authentification est éprouvé.
/// </summary>
internal interface IPlatformConnectionProbe
{
    /// <summary>Teste la plateforme (URL + clé API déchiffrée). Ne lève jamais d'exception : tout échec devient un diagnostic.</summary>
    PlatformTestResult Test(string platformUrl, string apiKey);
}
