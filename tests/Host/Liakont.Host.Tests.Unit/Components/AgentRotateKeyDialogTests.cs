namespace Liakont.Host.Tests.Unit.Components;

using System;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Liakont.Modules.Ingestion.Contracts.DTOs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Tests bUnit du dialogue de RENOUVELLEMENT de clé (WEB09) : phase 1 = confirmation (conséquence explicite —
/// invalidation immédiate de l'ancienne clé) ; phase 2 = affichage de la NOUVELLE clé une seule fois
/// (avertissement + copie + fermeture). Composant PUR : on prouve le wiring UI ↔ callbacks.
/// </summary>
public sealed class AgentRotateKeyDialogTests : BunitContext
{
    public AgentRotateKeyDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
    }

    [Fact]
    public void Closed_dialog_renders_nothing()
    {
        var cut = Render<AgentRotateKeyDialog>(p => p.Add(c => c.IsOpen, false));

        cut.FindAll("[data-testid='agent-rotate-dialog']").Should().BeEmpty();
    }

    [Fact]
    public void Confirm_phase_shows_immediate_invalidation_and_confirm_cancel_fire()
    {
        var confirmed = false;
        var cancelled = false;

        var cut = Render<AgentRotateKeyDialog>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.AgentName, "Poste comptable")
            .Add(c => c.OnConfirm, EventCallback.Factory.Create(this, () => confirmed = true))
            .Add(c => c.OnCancel, EventCallback.Factory.Create(this, () => cancelled = true)));

        cut.Find("[data-testid='agent-rotate-consequence']").TextContent.Should().Contain("IMMÉDIATEMENT");
        cut.FindAll("[data-testid='agent-rotate-key']").Should().BeEmpty("aucune clé n'est affichée avant la confirmation");

        cut.Find("[data-testid='agent-rotate-confirm']").Click();
        confirmed.Should().BeTrue();

        cut.Find("[data-testid='agent-rotate-cancel']").Click();
        cancelled.Should().BeTrue();
    }

    [Fact]
    public void Key_phase_shows_new_key_once_with_warning_copy_and_close()
    {
        var key = new AgentKeyIssuedDto { AgentId = Guid.NewGuid(), KeyPrefix = "lk_pub2", FullKey = "lk_pub2.secret-new" };
        string? copied = null;
        var closed = false;

        var cut = Render<AgentRotateKeyDialog>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.AgentName, "Poste comptable")
            .Add(c => c.IssuedKey, key)
            .Add(c => c.OnCopyKey, EventCallback.Factory.Create<string>(this, k => copied = k))
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find("[data-testid='agent-rotate-key']").TextContent.Should().Contain("lk_pub2.secret-new");
        cut.Find("[data-testid='agent-rotate-key-warning']").TextContent.Should().Contain("ne pourra plus être affichée");
        cut.FindAll("[data-testid='agent-rotate-confirm']").Should().BeEmpty("la phase de confirmation est remplacée par la clé");

        cut.Find("[data-testid='agent-rotate-copy']").Click();
        copied.Should().Be("lk_pub2.secret-new");

        cut.Find("[data-testid='agent-rotate-close']").Click();
        closed.Should().BeTrue();
    }

    [Fact]
    public void Error_message_is_displayed_in_confirm_phase()
    {
        var cut = Render<AgentRotateKeyDialog>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.AgentName, "Poste comptable")
            .Add(c => c.ErrorMessage, "Cet agent est introuvable dans votre parc."));

        cut.Find("[data-testid='agent-rotate-error']").TextContent.Should().Contain("introuvable");
    }
}
