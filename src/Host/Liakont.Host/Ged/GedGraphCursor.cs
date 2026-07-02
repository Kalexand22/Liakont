namespace Liakont.Host.Ged;

using System;

/// <summary>
/// Curseur keyset composite d'une page d'exploration de graphe GED, tel que consommé par la page
/// <c>/ged/objet</c> et sa vue-pure (projeté depuis <c>GraphCursor</c>, GED08). Identifie de façon UNIQUE la
/// dernière ligne d'une page (document atteint, entité de rattachement, rôle) — la pagination reprend APRÈS ce
/// triplet (RL-20, jamais d'OFFSET). Distinct du curseur d'application pour garder la page/vue libres de la
/// couche Application (F19 §6.7).
/// </summary>
public sealed record GedGraphCursor(Guid ManagedDocumentId, Guid EntityId, string Role);
