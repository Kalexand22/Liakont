namespace Liakont.Modules.DocumentApproval.Tests.Unit;

using System;
using System.Linq;
using FluentAssertions;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Domain;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Xunit;

/// <summary>
/// Machine FERMÉE par purpose (ADR-0028 §3/§4, INV-APPROVAL-2/3). Produit cartésien : aucune transition hors
/// graphe, aucun retour arrière depuis un terminal, sous-graphe self-billing = 4 états EXACTS.
/// </summary>
public sealed class ValidationMachineTests
{
    [Fact]
    public void No_Transition_Is_Allowed_From_A_Terminal_State_For_Any_Purpose()
    {
        foreach (var policy in ValidationPurposePolicy.All())
        {
            foreach (var from in Enum.GetValues<ValidationState>())
            {
                if (!IsTerminal(from))
                {
                    continue;
                }

                foreach (var to in Enum.GetValues<ValidationState>())
                {
                    policy.AllowsTransition(from, to).Should().BeFalse(
                        "aucun retour arrière depuis un terminal (purpose {0}, {1} → {2}) — INV-APPROVAL-2",
                        policy.Purpose, from, to);
                }
            }
        }
    }

    [Fact]
    public void SelfBilling_Subgraph_Is_Exactly_Four_Distinct_States()
    {
        var policy = ValidationPurposePolicy.For(ValidationPurpose.SelfBilledAcceptance);

        policy.AllowedStates.Should().BeEquivalentTo(new[]
        {
            ValidationState.PendingValidation,
            ValidationState.Validated,
            ValidationState.TacitlyValidated,
            ValidationState.Contested,
        });

        // ValidationInProgress / Expired / Rejected sont HORS du sous-graphe self-billing (ADR-0028 §4).
        policy.IsStateAllowed(ValidationState.ValidationInProgress).Should().BeFalse();
        policy.IsStateAllowed(ValidationState.Expired).Should().BeFalse();
        policy.IsStateAllowed(ValidationState.Rejected).Should().BeFalse("le self-billing utilise Contested, pas Rejected");
    }

    [Fact]
    public void SelfBilling_Allows_Only_Pending_To_Validated_Tacit_Or_Contested()
    {
        var policy = ValidationPurposePolicy.For(ValidationPurpose.SelfBilledAcceptance);

        policy.AllowsTransition(ValidationState.PendingValidation, ValidationState.Validated).Should().BeTrue();
        policy.AllowsTransition(ValidationState.PendingValidation, ValidationState.TacitlyValidated).Should().BeTrue();
        policy.AllowsTransition(ValidationState.PendingValidation, ValidationState.Contested).Should().BeTrue();

        policy.AllowsTransition(ValidationState.PendingValidation, ValidationState.ValidationInProgress).Should().BeFalse();
        policy.AllowsTransition(ValidationState.PendingValidation, ValidationState.Rejected).Should().BeFalse();
        policy.AllowsTransition(ValidationState.PendingValidation, ValidationState.Expired).Should().BeFalse();
    }

    [Fact]
    public void MandateSignature_Has_No_Tacit_Nor_Contested()
    {
        var policy = ValidationPurposePolicy.For(ValidationPurpose.MandateSignature);

        policy.IsStateAllowed(ValidationState.TacitlyValidated).Should().BeFalse("la signature de mandat est expresse (ADR-0028 §4)");
        policy.IsStateAllowed(ValidationState.Contested).Should().BeFalse("Contested est propre au self-billing");
        policy.AllowsTransition(ValidationState.PendingValidation, ValidationState.ValidationInProgress).Should().BeTrue();
        policy.AllowsTransition(ValidationState.ValidationInProgress, ValidationState.Validated).Should().BeTrue();
    }

    [Fact]
    public void Allowed_Transition_Always_Stays_Within_The_Purpose_Subgraph()
    {
        // Garde anti-dérive : une transition autorisée a ses DEUX extrémités dans le sous-graphe du purpose
        // (aucune arête « fuyant » hors du sous-graphe déclaré).
        foreach (var policy in ValidationPurposePolicy.All())
        {
            foreach (var from in Enum.GetValues<ValidationState>())
            {
                foreach (var to in Enum.GetValues<ValidationState>())
                {
                    if (!policy.AllowsTransition(from, to))
                    {
                        continue;
                    }

                    policy.IsStateAllowed(from).Should().BeTrue("({0}: {1} → {2})", policy.Purpose, from, to);
                    policy.IsStateAllowed(to).Should().BeTrue("({0}: {1} → {2})", policy.Purpose, from, to);
                }
            }
        }
    }

    private static bool IsTerminal(ValidationState state)
        => state is not (ValidationState.PendingValidation or ValidationState.ValidationInProgress);
}
