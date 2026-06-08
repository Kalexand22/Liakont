namespace Liakont.Host.Dashboard;

/// <summary>Compteur d'un état de document sur le tableau de bord (nom de l'état + nombre).</summary>
/// <param name="State">Nom de l'état (clé de <c>DocumentListResult.CountsByState</c>).</param>
/// <param name="Count">Nombre de documents dans cet état pour le tenant courant.</param>
public sealed record DashboardStateCount(string State, int Count);
