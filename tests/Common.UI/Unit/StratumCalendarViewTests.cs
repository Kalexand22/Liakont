namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class StratumCalendarViewTests : BunitContext
{
    public StratumCalendarViewTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // ── Rendering states ────────────────────────────────────────────
    [Fact]
    public void ShouldRenderEmptyStateWhenDataIsEmpty()
    {
        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Data, Array.Empty<CalendarItem>())
            .Add(v => v.DateProperty, "DueDate"));

        cut.Find(".stratum-calendar-view__empty").Should().NotBeNull();
        cut.Find(".stratum-calendar-view__empty-text").TextContent.Should().Contain("Aucun");
    }

    [Fact]
    public void ShouldRenderLoadingSkeletonWhenLoadingIsTrue()
    {
        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Loading, true));

        var loading = cut.Find(".stratum-calendar-view__loading");
        loading.GetAttribute("aria-busy").Should().Be("true");
        loading.GetAttribute("role").Should().Be("status");

        // 5 skeleton rows × 7 cells = 35
        cut.FindAll(".stratum-calendar-view__skeleton-cell").Count.Should().Be(35);
    }

    // ── Month view ──────────────────────────────────────────────────
    [Fact]
    public void ShouldRenderMonthViewByDefault()
    {
        var data = SampleData();

        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.DateProperty, "DueDate"));

        cut.Find(".stratum-calendar-view__month").Should().NotBeNull();
        cut.Find("[role='grid']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderWeekdayHeaders()
    {
        var data = SampleData();

        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.DateProperty, "DueDate"));

        var headers = cut.FindAll("[role='columnheader']");
        headers.Should().HaveCount(7);
        headers[0].TextContent.Should().Be("Lun");
        headers[6].TextContent.Should().Be("Dim");
    }

    [Fact]
    public void ShouldRenderItemsAsPillsOnMatchingDates()
    {
        var today = DateTime.Today;
        var data = new List<CalendarItem>
        {
            new(1, "Meeting", today),
            new(2, "Review", today),
        };

        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.DateProperty, "DueDate"));

        var dateStr = DateOnly.FromDateTime(today).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var pills = cut.FindAll($"[data-testid='cal-pill-{dateStr}-0'], [data-testid='cal-pill-{dateStr}-1']");
        pills.Should().HaveCount(2);
    }

    [Fact]
    public void ShouldShowMoreIndicatorWhenExceedingMaxItemsPerDay()
    {
        var today = DateTime.Today;
        var data = Enumerable.Range(1, 5)
            .Select(i => new CalendarItem(i, $"Item {i}", today))
            .ToList();

        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.DateProperty, "DueDate")
            .Add(v => v.MaxItemsPerDay, 3));

        var dateStr = DateOnly.FromDateTime(today).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var more = cut.Find($"[data-testid='cal-more-{dateStr}']");
        more.TextContent.Should().Contain("+2");
    }

    // ── View toggle ─────────────────────────────────────────────────
    [Fact]
    public void ShouldRenderViewToggleButtons()
    {
        var data = SampleData();

        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.DateProperty, "DueDate"));

        cut.Find("[data-testid='cal-mode-month']").Should().NotBeNull();
        cut.Find("[data-testid='cal-mode-week']").Should().NotBeNull();
        cut.Find("[data-testid='cal-mode-day']").Should().NotBeNull();

        // Month is active by default
        cut.Find("[data-testid='cal-mode-month']").GetAttribute("aria-pressed").Should().Be("true");
    }

    [Fact]
    public async Task ShouldSwitchToWeekView()
    {
        var data = SampleData();

        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.DateProperty, "DueDate"));

        await cut.Find("[data-testid='cal-mode-week']").ClickAsync(new MouseEventArgs());

        cut.Find(".stratum-calendar-view__week-view").Should().NotBeNull();
        cut.Find("[data-testid='cal-mode-week']").GetAttribute("aria-pressed").Should().Be("true");
    }

    // ── Navigation ────────────────────────────────────────────────
    [Fact]
    public void ShouldRenderNavigationButtons()
    {
        var data = SampleData();

        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.DateProperty, "DueDate"));

        cut.Find("[data-testid='cal-prev']").Should().NotBeNull();
        cut.Find("[data-testid='cal-today']").Should().NotBeNull();
        cut.Find("[data-testid='cal-next']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldRenderToolbarWithAriaLabel()
    {
        var data = SampleData();

        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.DateProperty, "DueDate"));

        var toolbar = cut.Find("[role='toolbar']");
        toolbar.GetAttribute("aria-label").Should().Be("Navigation du calendrier");
    }

    // ── Item double-click ─────────────────────────────────────────────
    [Fact]
    public async Task DoubleClickingPillShouldFireOnRowActivated()
    {
        var activatedItem = (CalendarItem?)null;
        var today = DateTime.Today;
        var data = new List<CalendarItem> { new(1, "Meeting", today) };

        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.DateProperty, "DueDate")
            .Add(v => v.OnRowActivated, EventCallback.Factory.Create<CalendarItem>(this, item => activatedItem = item)));

        var dateStr = DateOnly.FromDateTime(today).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var pill = cut.Find($"[data-testid='cal-pill-{dateStr}-0']");

        // Double-click = two rapid clicks on the same element
        await pill.ClickAsync(new MouseEventArgs());
        await pill.ClickAsync(new MouseEventArgs());

        activatedItem.Should().NotBeNull();
        activatedItem!.Id.Should().Be(1);
    }

    // ── ARIA roles ───────────────────────────────────────────────
    [Fact]
    public void DayCellsShouldHaveGridcellRole()
    {
        var data = SampleData();

        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.DateProperty, "DueDate"));

        cut.FindAll("[role='gridcell']").Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ShouldRenderPeriodLabelWithAriaLive()
    {
        var data = SampleData();

        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.DateProperty, "DueDate"));

        var label = cut.Find(".stratum-calendar-view__period-label");
        label.GetAttribute("aria-live").Should().Be("polite");
    }

    // ── TestId ───────────────────────────────────────────────────
    [Fact]
    public void ShouldUseDefaultTestId()
    {
        var data = SampleData();

        var cut = Render<StratumCalendarView<CalendarItem>>(p => p
            .Add(v => v.Data, data)
            .Add(v => v.DateProperty, "DueDate"));

        cut.Find("[data-testid='stratum-calendar-view']").Should().NotBeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────
    private static List<CalendarItem> SampleData(int count = 3) =>
        Enumerable.Range(0, count)
            .Select(i => new CalendarItem(i + 1, $"Event {i + 1}", DateTime.Today.AddDays(i)))
            .ToList();

    private sealed record CalendarItem(int Id, string Title, DateTime DueDate);
}
