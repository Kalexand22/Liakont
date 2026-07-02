// Liakont addition (GDF01): build-time contributor that populates the outbox event-type registry — not part of the original Stratum vendoring.
namespace Stratum.Common.Infrastructure.Outbox;

/// <summary>
/// Contribue les correspondances « type d'événement → payload CLR » d'un module à
/// <see cref="IEventTypeRegistry"/>. Les contributeurs sont appliqués À LA CONSTRUCTION du registre
/// (au build DI, par <c>AddStratumEvents</c>), donc AVANT le premier poll de
/// <see cref="OutboxWorker"/> : un événement dont le type est déclaré par un contributeur ne peut
/// jamais être vu « inconnu » puis marqué <c>processed</c> à vide au démarrage (course corrigée par
/// GDF01 — l'ancien motif <c>IHostedService</c> enregistrait les types trop tard, en concurrence avec
/// le worker). Un module expose son contributeur via
/// <c>services.AddSingleton&lt;IEventTypeRegistrar, MonRegistrar&gt;()</c>.
/// </summary>
public interface IEventTypeRegistrar
{
    /// <summary>Enregistre les types d'événements du module dans le registre fourni.</summary>
    void Register(IEventTypeRegistry registry);
}
