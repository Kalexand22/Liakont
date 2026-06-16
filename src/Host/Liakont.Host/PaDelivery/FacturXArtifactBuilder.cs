namespace Liakont.Host.PaDelivery;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.FacturX.Application;
using Liakont.Modules.Transmission.Contracts;

/// <summary>
/// Pont (COMPOSITION ROOT) entre le pipeline d'envoi et le module FacturX (FX07, F16 §6.1). Implémente
/// l'abstraction <see cref="IFacturXArtifactBuilder"/> de <c>Transmission.Contracts</c> en DÉLÉGUANT au port
/// <see cref="IFacturXBuilder"/> du module FacturX (couche Application). Le pipeline (module Pipeline) génère
/// ainsi « derrière IFacturXBuilder » SANS franchir la frontière Contracts-only (module-rules §3 / CLAUDE.md
/// n°14) : seul le Host, racine de composition, référence à la fois <c>Transmission.Contracts</c> et le
/// module FacturX. Ne renvoie que les octets du PDF/A-3 scellé (le <c>factur-x.xml</c> CII est embarqué
/// dedans) ; le plug-in PA générique les transmet tels quels, sans jamais régénérer (CLAUDE.md n°6).
/// </summary>
internal sealed class FacturXArtifactBuilder : IFacturXArtifactBuilder
{
    private readonly IFacturXBuilder _builder;

    /// <summary>Construit le pont sur le port de génération du module FacturX.</summary>
    /// <param name="builder">Port de génération Factur-X (FacturX.Application).</param>
    public FacturXArtifactBuilder(IFacturXBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>> BuildSealedArtifactAsync(PivotDocumentDto pivot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pivot);

        // La génération bloque (lève) si un élément obligatoire EN 16931 n'est ni porté ni dérivable
        // (ADR-0023 INV-FX-2) ; on propage — jamais de Factur-X tronqué transmis (CLAUDE.md n°3).
        var facturX = await _builder.BuildAsync(pivot, cancellationToken).ConfigureAwait(false);
        return facturX.PdfBytes;
    }
}
