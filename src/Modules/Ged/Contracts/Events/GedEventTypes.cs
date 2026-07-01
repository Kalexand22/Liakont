namespace Liakont.Modules.Ged.Contracts.Events;

/// <summary>
/// Identifiants (chaînes) des types d'événements d'intégration publiés par le module GED via l'outbox du socle
/// (GED05b). Constantes partagées par le producteur (handler d'ingestion GED), le registrar de types
/// (<c>GedEventTypeRegistrar</c>) et le consommateur durable — une seule source de vérité pour la valeur persistée
/// en base. Espace de types DISJOINT du canal fiscal (<c>IngestionEventTypes</c>), F19 §4.1.
/// </summary>
public static class GedEventTypes
{
    /// <summary>Document géré accepté (nouveau ou altéré) — déclenche l'indexation aval (consommateur GED).</summary>
    public const string ManagedDocumentReceived = "ged.managed-document.received";
}
