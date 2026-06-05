namespace Liakont.Agent.Core.Heartbeat;

/// <summary>
/// Sonde d'espace disque restant sur le volume de la file locale (F12 §2.5 « espace disque restant »).
/// Abstraite pour rester testable (aucun accès disque réel dans les tests du heartbeat) et pour que la
/// mesure soit BEST-EFFORT : une mesure indisponible renvoie <c>null</c>, jamais une exception — le
/// heartbeat n'échoue pas parce que le disque n'a pas pu être sondé.
/// </summary>
public interface IDiskFreeSpaceProbe
{
    /// <summary>Octets disponibles sur le volume surveillé, ou <c>null</c> si la mesure a échoué.</summary>
    long? GetAvailableFreeBytes();
}
