namespace Liakont.PaClients.Fake;

/// <summary>
/// Comportement que le plug-in factice applique aux opérations d'ENVOI (<c>SendDocumentAsync</c> /
/// <c>SendPaymentReportAsync</c>) — il permet de prouver, en test, que le PRODUIT réagit correctement
/// à chacune des familles d'issue d'une PA (acceptance PAA02 : succès / rejet / erreur silencieuse /
/// timeout). La capacité absente n'est PAS un scénario d'envoi : elle est pilotée par les
/// <see cref="FakePaClientOptions.Capabilities"/> et retournée comme résultat typé, jamais une
/// exception (PAA01). Les trois familles d'erreur viennent de F05 §4.1.
/// </summary>
public enum FakePaScenario
{
    /// <summary>Émis / accepté par la PA — l'envoi aboutit (état <c>Issued</c>).</summary>
    Success = 0,

    /// <summary>Rejet métier explicite (4xx + <c>errors[]</c>) — non re-tentable (état <c>RejectedByPa</c>).</summary>
    Rejected = 1,

    /// <summary>
    /// Erreur silencieuse : réponse HTTP 200 contenant tout de même <c>errors[]</c> (F05 §4.1). Le
    /// produit doit la DÉTECTER comme un rejet (état <c>RejectedByPa</c>) malgré le succès HTTP.
    /// </summary>
    SilentError = 2,

    /// <summary>Erreur technique (5xx) — re-tentable au prochain run (état <c>TechnicalError</c>).</summary>
    TechnicalError = 3,

    /// <summary>Timeout réseau — re-tentable, modélisé comme une erreur technique typée (F05 §4.1).</summary>
    Timeout = 4,
}
