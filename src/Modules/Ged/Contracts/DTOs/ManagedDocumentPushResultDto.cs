namespace Liakont.Modules.Ged.Contracts.DTOs;

/// <summary>
/// Résultat INDIVIDUEL de l'ingestion d'un document géré dans un lot (F19 §2.4). Un document invalide ou en doublon
/// ne fait JAMAIS échouer le lot entier : chaque document porte son propre verdict (miroir du canal fiscal
/// <c>DocumentPushResultDto</c>, mais DISJOINT).
/// </summary>
/// <param name="SourceReference">La référence source du document concerné.</param>
/// <param name="Status">Le verdict d'ingestion du document.</param>
public sealed record ManagedDocumentPushResultDto(string SourceReference, ManagedDocumentPushStatus Status);
