namespace Liakont.Host.Tests.Unit.Components;

using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Tests bUnit du dialogue de RÉVOCATION d'agent (WEB09) : conséquence EXPLICITE en français (avec le nom de
/// l'agent), confirmation / annulation câblées, désactivation pendant l'action et message d'erreur.
/// </summary>
public sealed class AgentRevokeDialogTests : BunitContext
{
    public AgentRevokeDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
    }

    [Fact]
    public void Closed_dialog_renders_nothing()
    {
        var cut = Render<AgentRevokeDialog>(p => p.Add(c => c.IsOpen, false));

        cut.FindAll("[data-testid='agent-revoke-dialog']").Should().BeEmpty();
    }

    [Fact]
    public void Open_shows_consequence_with_agent_name_and_confirm_cancel_fire()
    {
        var confirmed = false;
        var cancelled = false;

        var cut = Render<AgentRevokeDialog>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.AgentName, "Poste comptable")
            .Add(c => c.OnConfirm, EventCallback.Factory.Create(this, () => confirmed = true))
            .Add(c => c.OnCancel, EventCallback.Factory.Create(this, () => cancelled = true)));

        var consequence = cut.Find("[data-testid='agent-revoke-consequence']").TextContent;
        consequence.Should().Contain("Poste comptable");
        consequence.Should().Contain("ne pourra plus pousser de documents");

        cut.Find("[data-testid='agent-revoke-confirm']").Click();
        confirmed.Should().BeTrue();

        cut.Find("[data-testid='agent-revoke-cancel']").Click();
        cancelled.Should().BeTrue();
    }

    [Fact]
    public void Busy_disables_confirm_and_cancel()
    {
        var cut = Render<AgentRevokeDialog>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.AgentName, "Poste comptable")
            .Add(c => c.Busy, true));

        cut.Find("[data-testid='agent-revoke-confirm']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='agent-revoke-cancel']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Error_message_is_displayed()
    {
        var cut = Render<AgentRevokeDialog>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.AgentName, "Poste comptable")
            .Add(c => c.ErrorMessage, "Cet agent est introuvable dans votre parc."));

        cut.Find("[data-testid='agent-revoke-error']").TextContent.Should().Contain("introuvable");
    }
}
