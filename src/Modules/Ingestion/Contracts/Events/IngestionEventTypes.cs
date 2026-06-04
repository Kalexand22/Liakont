namespace Liakont.Modules.Ingestion.Contracts.Events;

/// <summary>
/// Identifiants (chaînes) des types d'événements d'intégration publiés par le module Ingestion via
/// l'outbox (PIV04). Constantes partagées par le producteur (handler), le registrar de types et les
/// consommateurs (PIP01, TRK03) — une seule source de vérité pour la valeur persistée en base.
/// </summary>
public static class IngestionEventTypes
{
    /// <summary>Document accepté (nouveau ou altéré) — déclenche le pipeline aval (PIP01).</summary>
    public const string DocumentReceived = "ingestion.document.received";

    /// <summary>Source altérée (référence connue, empreinte différente) — consommé par TRK03 (F06).</summary>
    public const string SourceAlterationDetected = "ingestion.source.altered";
}
