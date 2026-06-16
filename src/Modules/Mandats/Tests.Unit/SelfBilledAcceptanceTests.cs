namespace Liakont.Modules.Mandats.Tests.Unit;

using System.Collections.Generic;
using FluentAssertions;
using Liakont.Modules.Mandats.Domain.Entities;
using Xunit;

/// <summary>
/// Agrégat <see cref="SelfBilledAcceptance"/> (ADR-0024 §2, F15 §2.3) : état initial, machine FERMÉE
/// PendingAcceptance → {Accepted, TacitlyAccepted, Contested} prouvée par produit cartésien (INV-ACCEPT-4),
/// aucun retour arrière depuis un état terminal, état calculé « gate ouvert » (<see cref="SelfBilledAcceptance.IsAccepted"/>).
/// </summary>
public sealed class SelfBilledAcceptanceTests
{
    private static readonly DateTimeOffset PendingSince = new(2026, 6, 16, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Les trois transitions du graphe, appliquées à un agrégat.</summary>
    public enum AcceptanceAction
    {
        AcceptExpressly,
        AcceptTacitly,
        Contest,
    }

    public static IEnumerable<object[]> TransitionMatrix()
    {
        foreach (var state in Enum.GetValues<SelfBilledAcceptanceState>())
        {
            foreach (var action in Enum.GetValues<AcceptanceAction>())
            {
                yield return [state, action];
            }
        }
    }

    [Fact]
    public void Create_Starts_Pending_NotAccepted_NotTerminal()
    {
        var acceptance = SelfBilledAcceptance.Create(Guid.NewGuid(), Guid.NewGuid(), PendingSince, deadlineUtc: null);

        acceptance.State.Should().Be(SelfBilledAcceptanceState.PendingAcceptance);
        acceptance.IsAccepted.Should().BeFalse("PendingAcceptance ne doit pas ouvrir le gate.");
        acceptance.IsTerminal.Should().BeFalse();
        acceptance.AllocatedNumber.Should().BeNull("le BT-1 fiscal est alloué par MND05, jamais à la création.");
        acceptance.UpdatedAt.Should().BeNull("aucune transition n'a encore eu lieu.");
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", "11111111-1111-1111-1111-111111111111")]
    [InlineData("11111111-1111-1111-1111-111111111111", "00000000-0000-0000-0000-000000000000")]
    public void Create_Rejects_Empty_Identifiers(string companyId, string documentId)
    {
        var act = () => SelfBilledAcceptance.Create(Guid.Parse(companyId), Guid.Parse(documentId), PendingSince, null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Rejects_Deadline_Before_PendingSince()
    {
        var act = () => SelfBilledAcceptance.Create(
            Guid.NewGuid(), Guid.NewGuid(), PendingSince, deadlineUtc: PendingSince.AddSeconds(-1));
        act.Should().Throw<ArgumentException>("une échéance ne peut pas précéder l'entrée en attente.");
    }

    [Fact]
    public void Create_Accepts_Deadline_After_PendingSince()
    {
        var deadline = PendingSince.AddDays(30);
        var acceptance = SelfBilledAcceptance.Create(Guid.NewGuid(), Guid.NewGuid(), PendingSince, deadline);
        acceptance.DeadlineUtc.Should().Be(deadline);
    }

    [Fact]
    public void AcceptExpressly_From_Pending_Opens_Gate()
    {
        var acceptance = NewPending();
        acceptance.AcceptExpressly();

        acceptance.State.Should().Be(SelfBilledAcceptanceState.Accepted);
        acceptance.IsAccepted.Should().BeTrue();
        acceptance.IsTerminal.Should().BeTrue();
        acceptance.UpdatedAt.Should().NotBeNull("une transition horodate la mutation.");
    }

    [Fact]
    public void AcceptTacitly_From_Pending_Opens_Gate()
    {
        var acceptance = NewPending();
        acceptance.AcceptTacitly();

        acceptance.State.Should().Be(SelfBilledAcceptanceState.TacitlyAccepted);
        acceptance.IsAccepted.Should().BeTrue();
    }

    [Fact]
    public void Contest_From_Pending_Closes_Gate()
    {
        var acceptance = NewPending();
        acceptance.Contest();

        acceptance.State.Should().Be(SelfBilledAcceptanceState.Contested);
        acceptance.IsAccepted.Should().BeFalse("un document contesté ne doit pas être émis.");
        acceptance.IsTerminal.Should().BeTrue();
    }

    /// <summary>
    /// Produit cartésien (4 états de départ × 3 transitions) : une transition n'est permise QUE depuis
    /// PendingAcceptance et mène à l'état attendu ; depuis tout état terminal, la transition est rejetée
    /// et l'état reste inchangé (machine fermée, aucun retour arrière — INV-ACCEPT-4).
    /// </summary>
    [Theory]
    [MemberData(nameof(TransitionMatrix))]
    public void Closed_Machine_Allows_Only_From_Pending(
        SelfBilledAcceptanceState startState, AcceptanceAction action)
    {
        var acceptance = Reconstituted(startState);
        var apply = () => Apply(acceptance, action);

        if (startState == SelfBilledAcceptanceState.PendingAcceptance)
        {
            apply.Should().NotThrow();
            acceptance.State.Should().Be(ExpectedTarget(action));
        }
        else
        {
            apply.Should().Throw<InvalidOperationException>(
                "aucune transition n'est permise depuis un état terminal (INV-ACCEPT-4).");
            acceptance.State.Should().Be(startState, "une transition rejetée ne change pas l'état.");
        }
    }

    private static void Apply(SelfBilledAcceptance acceptance, AcceptanceAction action)
    {
        switch (action)
        {
            case AcceptanceAction.AcceptExpressly:
                acceptance.AcceptExpressly();
                break;
            case AcceptanceAction.AcceptTacitly:
                acceptance.AcceptTacitly();
                break;
            case AcceptanceAction.Contest:
                acceptance.Contest();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private static SelfBilledAcceptanceState ExpectedTarget(AcceptanceAction action) => action switch
    {
        AcceptanceAction.AcceptExpressly => SelfBilledAcceptanceState.Accepted,
        AcceptanceAction.AcceptTacitly => SelfBilledAcceptanceState.TacitlyAccepted,
        AcceptanceAction.Contest => SelfBilledAcceptanceState.Contested,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    private static SelfBilledAcceptance NewPending()
        => SelfBilledAcceptance.Create(Guid.NewGuid(), Guid.NewGuid(), PendingSince, deadlineUtc: null);

    private static SelfBilledAcceptance Reconstituted(SelfBilledAcceptanceState state)
        => SelfBilledAcceptance.Reconstitute(
            Guid.NewGuid(), Guid.NewGuid(), state, allocatedNumber: null,
            PendingSince, deadlineUtc: null, createdAt: PendingSince, updatedAt: null);
}
