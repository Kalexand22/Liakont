namespace Liakont.Host.Components;

using System;

/// <summary>
/// Ligne présentationnelle d'un agent d'extraction (nom, état, dernier contact, version) rendue par
/// <see cref="AgentStatusList"/>. Modèle PARTAGÉ des synthèses du tableau de bord et du hub
/// Paramétrage (lot polish UX/UI : remplace deux records identiques dupliqués — DashboardAgentLine
/// et ParametrageAgentLine).
/// </summary>
public sealed record AgentStatusLine(string Name, DateTimeOffset? LastSeenUtc, string? Version, bool IsRevoked);
