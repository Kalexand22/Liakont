namespace Liakont.Modules.Archive.Application;

using System;

/// <summary>
/// Contexte de scellement d'une entrée, dérivé sous verrou par <see cref="IArchiveEntryStore"/> : le
/// maillon de chaîne et l'horodatage d'archivage (strictement croissant par tenant), à inscrire dans le
/// manifest AVANT l'insertion de la ligne.
/// </summary>
/// <param name="ChainHash">Maillon <c>chain_hash(N)</c> (hex minuscule).</param>
/// <param name="ArchivedUtc">Horodatage d'archivage (UTC), strictement croissant dans le tenant.</param>
public sealed record ArchiveSealContext(string ChainHash, DateTimeOffset ArchivedUtc);
