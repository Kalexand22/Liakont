namespace Liakont.Modules.Archive.Contracts;

using System;

/// <summary>
/// Ajout d'un ADDENDUM à un paquet GED existant (F19 §5.1) : une pièce ajoutée APRÈS coup ne réécrit JAMAIS
/// le paquet (WORM) — elle est rangée write-once comme un nouveau fichier, décrit par un manifest-addendum.
/// Le paquet cible est localisé par sa clé GÉNÉRIQUE (même arborescence <c>_ged/{kind}/{année}/{mois}/{clé}/</c>
/// que le paquet initial). Contrairement à l'addendum fiscal (<see cref="ArchiveAddendumRequest"/>), il n'entre
/// dans AUCUNE chaîne de hashes (option C) ; son idempotence est portée par son contenu, son <c>Kind</c> ET le
/// nom de sa pièce (deux addenda au contenu identique mais de <c>Kind</c> ou de nom différent sont distincts).
/// </summary>
/// <param name="ArchiveKind">Nature générique du paquet cible (même valeur que le paquet initial).</param>
/// <param name="ArchiveKey">Clé d'arborescence du paquet cible.</param>
/// <param name="FiledOn">Date de rangement du paquet cible (année/mois de l'arborescence).</param>
/// <param name="Kind">Nature de l'addendum (générique), tracée dans le manifest-addendum.</param>
/// <param name="Attachment">La pièce ajoutée (exacte) — un seul fichier par addendum.</param>
public sealed record GedArchiveAddendumRequest(
    string ArchiveKind,
    string ArchiveKey,
    DateOnly FiledOn,
    string Kind,
    ArchiveAttachment Attachment);
