namespace Liakont.Modules.Ged.Application.Index;

using System;

/// <summary>
/// Un document atteint par la traversée : l'entité de rattachement (<see cref="EntityId"/>), le rôle du lien
/// (<see cref="Role"/>) et la profondeur MINIMALE à laquelle l'entité a été atteinte depuis la racine.
/// </summary>
public sealed record GraphDocumentHit
{
    /// <summary>Document atteignable.</summary>
    public required Guid ManagedDocumentId { get; init; }

    /// <summary>Entité (dans le voisinage atteint) qui rattache ce document.</summary>
    public required Guid EntityId { get; init; }

    /// <summary>Rôle du lien document↔entité.</summary>
    public required string Role { get; init; }

    /// <summary>Profondeur minimale de l'entité depuis la racine (0 = racine).</summary>
    public required int Depth { get; init; }
}
