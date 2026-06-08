namespace Liakont.Host.Parametrage;

using System;

/// <summary>
/// Ligne d'agent du tenant pour la section « Agents » de la page Paramétrage (WEB04b), en LECTURE
/// seule (la gestion du parc — enregistrement, révocation, rotation — est l'écran WEB09). Ne porte
/// AUCUN secret (ni clé ni empreinte).
/// </summary>
/// <param name="Name">Nom de l'agent.</param>
/// <param name="LastSeenUtc">Dernier heartbeat reçu (UTC), ou <c>null</c> si jamais signalé.</param>
/// <param name="Version">Dernière version d'agent vue, ou <c>null</c>.</param>
/// <param name="IsRevoked"><c>true</c> si l'agent est révoqué.</param>
public sealed record ParametrageAgentLine(string Name, DateTimeOffset? LastSeenUtc, string? Version, bool IsRevoked);
