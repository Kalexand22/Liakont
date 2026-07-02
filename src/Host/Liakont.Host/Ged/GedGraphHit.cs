namespace Liakont.Host.Ged;

using System;

/// <summary>
/// Un document ATTEIGNABLE par la traversée de graphe depuis la racine (F19 §6.4), tel que consommé par la
/// vue-pure <c>GedGraphView</c> (projeté depuis <c>GraphDocumentHit</c>, GED08). Porte l'entité de rattachement
/// (<see cref="EntityId"/>), le rôle du lien document↔entité (<see cref="Role"/>) et la profondeur MINIMALE à
/// laquelle l'entité a été atteinte (<see cref="Depth"/>, 0 = la racine elle-même). Le document masqué côté
/// serveur (entité confidentielle sans le droit) n'est jamais présent ici.
/// </summary>
public sealed record GedGraphHit(Guid DocumentId, Guid EntityId, string Role, int Depth);
