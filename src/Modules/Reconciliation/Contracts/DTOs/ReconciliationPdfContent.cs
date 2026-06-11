namespace Liakont.Modules.Reconciliation.Contracts.DTOs;

using System.IO;

/// <summary>
/// Contenu d'un PDF de la file de réconciliation, pour AFFICHAGE dans la console (API04 GET
/// <c>/reconciliation/{id}/pdf</c>, page WEB08). Le flux est STREAMÉ vers la réponse HTTP (binaire
/// volumineux — même motif que <c>IIngestedPdfStore.OpenPooledPdfAsync</c> qui expose un
/// <see cref="Stream"/> sur sa surface Contracts). L'appelant (l'endpoint) DISPOSE le flux après écriture.
/// </summary>
/// <param name="Content">Flux en LECTURE SEULE du PDF du pool ; disposé par l'appelant.</param>
/// <param name="FileName">Nom de fichier lisible du PDF (en-tête <c>Content-Disposition</c>).</param>
public sealed record ReconciliationPdfContent(Stream Content, string FileName);
