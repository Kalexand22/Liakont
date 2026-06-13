namespace Liakont.Host.Dashboard;

/// <summary>
/// Un point du graphique « Année en cours » du tableau de bord : libellé d'état (français, axe X)
/// et nombre de documents. Propriétés nommées lues par réflexion par le composant <c>Chart</c> du
/// design-system (<c>CategoryField</c>/<c>ValueField</c>).
/// </summary>
public sealed record DashboardChartPoint(string Etat, int Nombre);
