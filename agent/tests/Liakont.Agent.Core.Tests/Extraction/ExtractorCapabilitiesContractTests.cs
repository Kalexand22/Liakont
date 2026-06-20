namespace Liakont.Agent.Core.Tests.Extraction;

using FluentAssertions;
using Liakont.Agent.Core.Extraction;
using Xunit;

/// <summary>
/// Contrat du drapeau R9 « gate document finalisé » (<see cref="ExtractorCapabilities.ExtractsOnlyFinalizedDocuments"/>,
/// ADR-0004 D4 Famille 2). Le DÉFAUT doit être <c>false</c> (fail-closed) : un adaptateur qui n'a pas
/// DÉCLARÉ extraire uniquement des documents finalisés ne doit jamais être présumé conforme — la garde
/// plateforme (différée, RD403) bloque plutôt que de risquer l'envoi d'un brouillon (CLAUDE.md n°3).
/// </summary>
public class ExtractorCapabilitiesContractTests
{
    [Fact]
    public void ExtractsOnlyFinalizedDocuments_defaults_to_false_fail_closed()
    {
        new ExtractorCapabilities().ExtractsOnlyFinalizedDocuments.Should().BeFalse();
    }

    [Fact]
    public void ExtractsOnlyFinalizedDocuments_is_carried_when_declared()
    {
        new ExtractorCapabilities(extractsOnlyFinalizedDocuments: true)
            .ExtractsOnlyFinalizedDocuments.Should().BeTrue();
    }

    [Fact]
    public void FlowKind_defaults_to_unit_invoice_simplest_form()
    {
        // Slot capacité RÉSERVÉ (ADR-0004 D4 Famille 3 / §5, RD406) : défaut sûr = forme la plus simple
        // (chaque document est une facture unitaire). Un connecteur existant déclare implicitement ce défaut.
        new ExtractorCapabilities().FlowKind.Should().Be(FlowKind.UnitInvoice);
    }

    [Fact]
    public void FlowKind_is_carried_when_declared_aggregated()
    {
        // Réservé pour un futur connecteur POS B2C agrégé (Z journalier) : le slot existe déjà, l'ajout
        // d'un tel connecteur est additif, jamais une rupture (Conséquence §5).
        new ExtractorCapabilities(flowKind: FlowKind.AggregatedReporting)
            .FlowKind.Should().Be(FlowKind.AggregatedReporting);
    }
}
