namespace Liakont.Modules.Transmission.Contracts;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;

/// <summary>
/// Pont de génération de l'artefact Factur-X CONSOMMÉ par le pipeline d'envoi (FX07, F16 §6.1/§7).
/// <para>
/// Pourquoi ce pont vit ici (et pas une référence directe à <c>IFacturXBuilder</c>) : le pipeline (module
/// Pipeline) n'accède aux autres modules QUE par leurs <c>Contracts</c> (frontière Contracts-only,
/// module-rules §3 / CLAUDE.md n°14). Le port de génération <c>IFacturXBuilder</c> du module FacturX vit en
/// couche <c>Application</c> — l'atteindre depuis le pipeline serait une violation de frontière (P1). Ce
/// pont est donc défini dans <c>Transmission.Contracts</c> (là où vivent déjà la capacité
/// <see cref="PaCapabilities.SupportsFacturXTransmission"/> qui le déclenche et le précédent
/// <see cref="IDocumentDeliveryChannel"/>), et IMPLÉMENTÉ AU HOST (composition root) en délégant à
/// <c>IFacturXBuilder</c> : « derrière IFacturXBuilder », sans franchir la frontière.
/// </para>
/// <para>
/// La génération reste 100 % plateforme et DÉTERMINISTE du pivot (ADR-0023 INV-FX-4) : ce contrat ne
/// consulte aucune <see cref="PaCapabilities"/>. La DÉCISION de générer appartient au pipeline appelant,
/// pilotée par la capacité de la PA active (jamais <c>if (pa is Generique)</c>, CLAUDE.md n°8).
/// </para>
/// </summary>
public interface IFacturXArtifactBuilder
{
    /// <summary>
    /// Construit le Factur-X scellé (PDF/A-3 + <c>factur-x.xml</c> CII EN 16931 embarqué) à partir du pivot
    /// SEUL et renvoie ses octets, prêts à être transmis tels quels par le plug-in PA. BLOQUE (lève) si un
    /// élément obligatoire n'est ni porté ni dérivable par agrégation normative — jamais de Factur-X tronqué
    /// ni de valeur fiscale fabriquée (ADR-0023 INV-FX-2 ; CLAUDE.md n°3).
    /// </summary>
    /// <param name="pivot">Le document pivot EN 16931 à sceller en Factur-X.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Les octets du Factur-X PDF/A-3 scellé.</returns>
    Task<ReadOnlyMemory<byte>> BuildSealedArtifactAsync(PivotDocumentDto pivot, CancellationToken cancellationToken = default);
}
