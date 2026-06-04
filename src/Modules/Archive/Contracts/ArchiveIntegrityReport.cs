namespace Liakont.Modules.Archive.Contracts;

using System.Collections.Generic;

/// <summary>
/// Rapport d'intégrité de la chaîne d'archive d'un tenant (TRK05 §3-§5 ; étendu par l'ArchiveVerifier de
/// TRK06). Recalcule, dans l'ordre, l'empreinte de chaque paquet/addendum depuis le contenu RÉEL du
/// coffre, puis le chaînage <c>chain_hash</c>, et compare aux valeurs scellées en base. Toute altération
/// d'une pièce (paquet ou addendum) casse la chaîne à partir de ce point.
/// </summary>
/// <param name="IsIntact"><c>true</c> si toutes les entrées sont valides (contenu + chaînage).</param>
/// <param name="EntryCount">Nombre d'entrées vérifiées.</param>
/// <param name="Entries">Détail par entrée, dans l'ordre de la chaîne.</param>
/// <param name="FirstBreakDetail">Message français du premier maillon rompu, ou <c>null</c> si intact.</param>
public sealed record ArchiveIntegrityReport(
    bool IsIntact,
    int EntryCount,
    IReadOnlyList<ArchiveIntegrityEntry> Entries,
    string? FirstBreakDetail);
