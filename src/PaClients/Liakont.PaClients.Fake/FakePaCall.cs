namespace Liakont.PaClients.Fake;

/// <summary>
/// Une entrée du journal d'appels du plug-in factice — le pipeline peut ainsi être AUDITÉ en
/// assertion de test (acceptance PAA02 : « journal des appels exploitable en assertion »). Porte le
/// nom de la méthode appelée et un détail facultatif (numéro de document, type de flux…) sans aucune
/// donnée sensible.
/// </summary>
/// <param name="Method">Nom de la méthode <c>IPaClient</c> appelée (ex. « SendDocumentAsync »).</param>
/// <param name="Detail">Détail facultatif de l'appel (numéro de document, flux…), ou <c>null</c>.</param>
public sealed record FakePaCall(string Method, string? Detail = null);
