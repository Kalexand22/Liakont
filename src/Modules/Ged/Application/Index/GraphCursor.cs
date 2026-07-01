namespace Liakont.Modules.Ged.Application.Index;

using System;

/// <summary>
/// Curseur keyset composite d'une exploration de graphe : identifie de façon UNIQUE une ligne de résultat
/// (document atteint, entité de rattachement, rôle). L'ordre est lexicographique sur ce triplet.
/// </summary>
public sealed record GraphCursor(Guid ManagedDocumentId, Guid EntityId, string Role);
