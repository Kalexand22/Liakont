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
/// Tests bUnit du dialogue d'ENREGISTREMENT d'agent (WEB09) : nom obligatoire (soumission bloquée tant qu'il
/// est vide), soumission avec le nom saisi, et affichage de la clé UNE seule fois (avertissement + copie +
/// fermeture). Composant PUR : on prouve le wiring UI ↔ callbacks, sans service ni base.
/// </summary>
public sealed class AgentRegisterDialogTests : BunitContext
{
    public AgentRegisterDialogTests()
    {
        // StratumButton (RadzenButton) peut appeler du JS : mode permissif, comme les autres tests de pages.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
    }

    [Fact]
    public void Closed_dialog_renders_nothing()
    {
        var cut = Render<AgentRegisterDialog>(p => p.Add(c => c.IsOpen, false));

        cut.FindAll("[data-testid='agent-register-dialog']").Should().BeEmpty();
    }

    [Fact]
    public void Submit_is_blocked_until_a_name_is_entered_then_fires_with_the_name()
    {
        string? submitted = null;

        var cut = Render<AgentRegisterDialog>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.OnSubmit, EventCallback.Factory.Create<string>(this, n => submitted = n)));

        cut.Find("[data-testid='agent-register-submit']").HasAttribute("disabled").Should().BeTrue(
            "le nom est obligatoire : la soumission est bloquée tant qu'il est vide");

        cut.Find("[data-testid='agent-register-name']").Input("Poste comptable");

        cut.Find("[data-testid='agent-register-submit']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='agent-register-submit']").Click();

        submitted.Should().Be("Poste comptable");
    }

    [Fact]
    public void Issued_key_is_shown_once_with_warning_copy_and_close()
    {
        var key = new AgentKeyIssuedDto { AgentId = Guid.NewGuid(), KeyPrefix = "lk_pub", FullKey = "lk_pub.secret-xyz" };
        string? copied = null;
        var closed = false;

        var cut = Render<AgentRegisterDialog>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.IssuedKey, key)
            .Add(c => c.OnCopyKey, EventCallback.Factory.Create<string>(this, k => copied = k))
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find("[data-testid='agent-register-key']").TextContent.Should().Contain("lk_pub.secret-xyz");
        cut.Find("[data-testid='agent-register-key-warning']").TextContent.Should().Contain("ne pourra plus être affichée");
        cut.FindAll("[data-testid='agent-register-name']").Should().BeEmpty("le formulaire est remplacé par l'affichage de la clé");

        cut.Find("[data-testid='agent-register-copy']").Click();
        copied.Should().Be("lk_pub.secret-xyz", "la copie porte la clé complète");

        cut.Find("[data-testid='agent-register-close']").Click();
        closed.Should().BeTrue();
    }

    [Fact]
    public void Error_message_is_displayed()
    {
        var cut = Render<AgentRegisterDialog>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.ErrorMessage, "Le nom de l'agent est obligatoire."));

        cut.Find("[data-testid='agent-register-error']").TextContent.Should().Contain("obligatoire");
    }

    [Fact]
    public void Cancel_fires_close()
    {
        var closed = false;

        var cut = Render<AgentRegisterDialog>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find("[data-testid='agent-register-cancel']").Click();

        closed.Should().BeTrue();
    }
}
