namespace Liakont.PaClients.Contract.Tests;

/// <summary>
/// Issue d'un envoi vers une PA, exprimée dans le vocabulaire NEUTRE de la suite de contrat (découplé
/// du <c>FakePaScenario</c> du plug-in factice). Chaque plug-in sait produire son client dans chacune
/// de ces issues — le Fake via ses options en mémoire, B2Brouter / Super PDP via un mock HTTP
/// (testing-strategy §6). Couvre les trois familles d'erreur de F05 §4.1 (réseau re-tentable vs rejet
/// métier non re-tentable vs erreur silencieuse 200 + errors[]).
/// </summary>
public enum PaSendOutcome
{
    /// <summary>La PA accepte / émet le document (état attendu <c>Issued</c>).</summary>
    Success = 0,

    /// <summary>Rejet métier explicite (4xx + errors[]) — non re-tentable (état attendu <c>RejectedByPa</c>).</summary>
    Rejected = 1,

    /// <summary>
    /// Erreur silencieuse : succès au niveau transport (HTTP 200) MAIS errors[] non vide (F05 §4.1).
    /// Le produit doit la DÉTECTER comme un rejet (état attendu <c>RejectedByPa</c>), pas comme une émission.
    /// </summary>
    SilentError = 2,

    /// <summary>Erreur technique (réseau, 5xx) — re-tentable au prochain run (état attendu <c>TechnicalError</c>).</summary>
    TechnicalError = 3,

    /// <summary>Timeout réseau — re-tentable, modélisé comme une erreur technique typée (F05 §4.1).</summary>
    Timeout = 4,
}
