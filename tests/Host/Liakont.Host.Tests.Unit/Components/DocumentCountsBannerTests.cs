namespace Liakont.Host.Tests.Unit.Components;

using System.Collections.Generic;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components;
using Microsoft.AspNetCore.Components;
using Xunit;

public sealed class DocumentCountsBannerTests : BunitContext
{
    public DocumentCountsBannerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Should_Render_Only_The_Present_States_And_Hide_The_Zeros()
    {
        var cut = Render<DocumentCountsBanner>(p => p
            .Add(b => b.Counts, new Dictionary<string, int> { ["Issued"] = 7, ["Blocked"] = 3 }));

        // Lot 2 : seuls les états PRÉSENTS sont affichés (« 0 Rejeté » noyait l'information) + « Tous ».
        cut.FindAll("[data-testid='doc-counts-all']").Should().ContainSingle();
        cut.Find("[data-testid='doc-counts-Issued']").TextContent.Should().Contain("7");
        cut.Find("[data-testid='doc-counts-Blocked']").TextContent.Should().Contain("3");
        foreach (var state in DocumentStateDisplay.CanonicalOrder)
        {
            if (state is "Issued" or "Blocked")
            {
                continue;
            }

            cut.FindAll($"[data-testid='doc-counts-{state}']").Should().BeEmpty($"l'état {state} est à zéro");
        }
    }

    [Fact]
    public void The_Selected_State_Chip_Should_Stay_Visible_Even_At_Zero()
    {
        // Drill-down d'URL vers un périmètre vide (ex. /documents?etat=Issued sur un mois sans émission) :
        // la pastille de l'état FILTRÉ reste visible (à zéro, active) pour rester désélectionnable.
        var cut = Render<DocumentCountsBanner>(p => p
            .Add(b => b.Counts, new Dictionary<string, int> { ["Blocked"] = 3 })
            .Add(b => b.SelectedState, "Issued"));

        var chip = cut.Find("[data-testid='doc-counts-Issued']");
        chip.TextContent.Should().Contain("0");
        chip.GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public void Total_Chip_Should_Sum_The_Counts()
    {
        var cut = Render<DocumentCountsBanner>(p => p
            .Add(b => b.Counts, new Dictionary<string, int> { ["Issued"] = 7, ["Blocked"] = 3 }));

        cut.Find("[data-testid='doc-counts-all']").TextContent.Should().Contain("10");
    }

    [Fact]
    public void Clicking_A_State_Chip_Should_Raise_The_Callback_With_That_State()
    {
        string? selected = "none";
        var cut = Render<DocumentCountsBanner>(p => p
            .Add(b => b.Counts, new Dictionary<string, int> { ["Blocked"] = 3 })
            .Add(b => b.OnStateSelected, EventCallback.Factory.Create<string?>(this, s => selected = s)));

        cut.Find("[data-testid='doc-counts-Blocked']").Click();

        selected.Should().Be("Blocked");
    }

    [Fact]
    public void Clicking_Tous_Should_Raise_The_Callback_With_Null()
    {
        string? selected = "Blocked";
        var cut = Render<DocumentCountsBanner>(p => p
            .Add(b => b.Counts, new Dictionary<string, int> { ["Blocked"] = 3 })
            .Add(b => b.SelectedState, "Blocked")
            .Add(b => b.OnStateSelected, EventCallback.Factory.Create<string?>(this, s => selected = s)));

        cut.Find("[data-testid='doc-counts-all']").Click();

        selected.Should().BeNull();
    }

    [Fact]
    public void Should_Mark_The_Selected_State_As_Active()
    {
        var cut = Render<DocumentCountsBanner>(p => p
            .Add(b => b.Counts, new Dictionary<string, int> { ["Issued"] = 1 })
            .Add(b => b.SelectedState, "Issued"));

        cut.Find("[data-testid='doc-counts-Issued']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='doc-counts-all']").GetAttribute("aria-pressed").Should().Be("false");
    }
}
