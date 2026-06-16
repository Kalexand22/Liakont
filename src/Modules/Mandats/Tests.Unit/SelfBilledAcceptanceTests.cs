namespace Liakont.Modules.Mandats.Tests.Unit;

using FluentAssertions;
using Liakont.Modules.Mandats.Domain.Entities;
using Liakont.Modules.Mandats.Infrastructure;
using Xunit;

/// <summary>
/// SIG05 — l'acceptation 389 est désormais une PROJECTION restreinte du module générique DocumentApproval
/// (purpose SelfBilledAcceptance, ADR-0028 §4) : la machine FERMÉE à 4 états et l'absence de retour arrière
/// (INV-ACCEPT-4) sont portées par DocumentApproval (prouvées par ses propres tests cartésiens) et re-prouvées
/// de bout en bout par les tests d'intégration Mandats (commandes + journal). Ici on garde STABLE la PROJECTION
/// vers le vocabulaire fiscal (<see cref="SelfBilledAcceptanceState"/>) : la correspondance est RESTREINTE aux
/// 4 états self-billing — tout état de validation hors du sous-graphe (ValidationInProgress/Rejected/Expired)
/// est REJETÉ (jamais projeté silencieusement, garde anti-faux-vert).
/// </summary>
public sealed class SelfBilledAcceptanceTests
{
    [Theory]
    [InlineData("PendingValidation", SelfBilledAcceptanceState.PendingAcceptance)]
    [InlineData("Validated", SelfBilledAcceptanceState.Accepted)]
    [InlineData("TacitlyValidated", SelfBilledAcceptanceState.TacitlyAccepted)]
    [InlineData("Contested", SelfBilledAcceptanceState.Contested)]
    public void Projects_The_Four_SelfBilling_States(string validationStateName, SelfBilledAcceptanceState expected)
    {
        SelfBilledAcceptanceStateMap.FromValidationStateName(validationStateName).Should().Be(expected);
    }

    [Theory]
    [InlineData("ValidationInProgress")]
    [InlineData("Rejected")]
    [InlineData("Expired")]
    [InlineData("Inconnu")]
    public void Rejects_States_Outside_The_SelfBilling_Subgraph(string validationStateName)
    {
        var act = () => SelfBilledAcceptanceStateMap.FromValidationStateName(validationStateName);
        act.Should().Throw<InvalidOperationException>(
            "le sous-graphe self-billing n'a que 4 états (ADR-0028 §4) ; tout autre état ne doit pas être projeté.");
    }

    [Fact]
    public void NameOrNull_Maps_Null_Genesis_To_Null()
    {
        SelfBilledAcceptanceStateMap.NameOrNull(null).Should().BeNull("la genèse du journal a un from_state null.");
        SelfBilledAcceptanceStateMap.NameOrNull("Validated").Should().Be(SelfBilledAcceptanceState.Accepted.ToString());
    }

    [Theory]
    [InlineData(SelfBilledAcceptanceState.Accepted, true)]
    [InlineData(SelfBilledAcceptanceState.TacitlyAccepted, true)]
    [InlineData(SelfBilledAcceptanceState.PendingAcceptance, false)]
    [InlineData(SelfBilledAcceptanceState.Contested, false)]
    public void IsAccepted_Opens_Gate_Only_For_Accepted_Or_TacitlyAccepted(
        SelfBilledAcceptanceState state, bool expected)
    {
        SelfBilledAcceptanceStateMap.IsAccepted(state).Should().Be(expected);
    }
}
