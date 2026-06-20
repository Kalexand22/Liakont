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
}
