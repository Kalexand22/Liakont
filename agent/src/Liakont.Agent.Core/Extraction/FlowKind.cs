namespace Liakont.Agent.Core.Extraction;

/// <summary>
/// Nature du flux porté par la source (capacité déclarée RÉSERVÉE — ADR-0004 D4 Famille 3 / §5, RD406).
/// L'ADR classe « Caisse / POS retail B2C agrégé (flux 10.3, Z journalier) » comme une <b>capacité</b>
/// <c>FlowKind</c> distinguant la facture unitaire de l'e-reporting agrégé. Slot réservé pour qu'un futur
/// connecteur POS soit un <i>ajout</i>, jamais une <i>rupture</i> (Conséquence §5). INERTE en V1 : aucun
/// consommateur plateforme ne le lit encore — l'agent ne fait que DÉCLARER la forme observée (CLAUDE.md
/// n°6), jamais l'interpréter ; le câblage transport/persistance est l'étape additive du futur connecteur.
/// </summary>
public enum FlowKind
{
    /// <summary>Facture unitaire — un document = une opération (cas le plus simple, défaut sûr).</summary>
    UnitInvoice = 1,

    /// <summary>E-reporting agrégé — la source émet un agrégat périodique (ex. ticket Z journalier d'une caisse POS B2C).</summary>
    AggregatedReporting = 2,
}
