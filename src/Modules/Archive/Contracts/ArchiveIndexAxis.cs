namespace Liakont.Modules.Archive.Contracts;

/// <summary>
/// Projection PLATE et locale au module Archive d'un axe d'index pour un document GED archivé (F19 §5.1).
/// Volontairement DISJOINTE du type <c>DocumentAxisLink</c> du module GED : <c>Archive.Contracts</c> ne
/// référence JAMAIS un type d'un autre module métier (frontière module-rules §3, CLAUDE.md n°14) — la couche
/// GED convertit ses <c>DocumentAxisLink</c> vers cette projection AU POINT D'APPEL (pattern « projections
/// locales, aucun Contracts→Contracts d'un autre module », API01c).
///
/// RL-19 (P1) : pour un axe CONFIDENTIEL, <see cref="Value"/> est <c>null</c> — on ne fige JAMAIS une valeur
/// confidentielle en clair dans le manifest WORM (un axe requalifié confidentiel après scellement resterait
/// sinon en clair, irréversible sous write-once). Seuls le code de l'axe et son caractère confidentiel sont
/// conservés dans le coffre ; la valeur reste dans l'index GED (chiffrable, D9).
/// </summary>
/// <param name="AxisCode">Code de l'axe (paramétrage tenant — jamais un littéral métier en dur, F19 §7).</param>
/// <param name="Value">Valeur indexée en clair, ou <c>null</c> si l'axe est confidentiel (RL-19).</param>
/// <param name="IsConfidential">Vrai si l'axe est confidentiel (sa valeur n'est jamais gelée en clair).</param>
public readonly record struct ArchiveIndexAxis(string AxisCode, string? Value, bool IsConfidential);
