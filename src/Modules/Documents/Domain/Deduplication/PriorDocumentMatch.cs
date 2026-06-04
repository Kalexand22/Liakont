namespace Liakont.Modules.Documents.Domain.Deduplication;

using Liakont.Modules.Documents.Domain.Entities;

/// <summary>
/// Antécédent trouvé en base pour la clé fonctionnelle <c>(supplier_siren, document_number)</c> d'un
/// document candidat (item TRK03, F06 §4) : son identifiant et son état. Le candidat lui-même est
/// EXCLU des antécédents (un document ne se compare jamais à lui-même).
/// </summary>
public readonly record struct PriorDocumentMatch(Guid Id, DocumentState State);
