namespace Liakont.Host.Dashboard;

using System;

/// <summary>Ligne d'agent du tenant pour la synthèse d'état du tableau de bord (sans aucun secret).</summary>
/// <param name="Name">Nom de l'agent.</param>
/// <param name="LastSeenUtc">Dernier heartbeat reçu (UTC), ou <c>null</c> si jamais signalé.</param>
/// <param name="Version">Dernière version d'agent vue, ou <c>null</c>.</param>
/// <param name="IsRevoked"><c>true</c> si l'agent est révoqué.</param>
public sealed record DashboardAgentLine(string Name, DateTimeOffset? LastSeenUtc, string? Version, bool IsRevoked);
