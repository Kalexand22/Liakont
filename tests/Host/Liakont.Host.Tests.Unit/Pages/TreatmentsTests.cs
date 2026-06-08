namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Modules.Pipeline.Contracts;
using Liakont.Modules.Pipeline.Contracts.Queries;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Grid;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI;
using Xunit;

public sealed class TreatmentsTests : BunitContext
{
    public TreatmentsTests()
    {
        // Radzen / grid components lean on JS interop — loose mode catches all calls.
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddLogging();
        Services.AddLocalization();

        // Production Common.UI graph (toast, tab manager, persistent selection, display templates…)
        // so the full DeclaredListPage → StratumDataGrid tree resolves as it does at runtime.
        Services.AddCommonUI();

        // Anonymous actor (UserId == Guid.Empty) so DeclaredListPage short-circuits all
        // per-user preference reads/writes — no database required for the render.
        Services.AddSingleton<IActorContextAccessor>(new FakeActorContextAccessor());
        Services.AddScoped<IGridPreferenceService, NoOpGridPreferenceService>();
        Services.AddScoped<ISavedFilterService, NoOpSavedFilterService>();
    }

    [Fact]
    public void Shows_An_Explicit_Empty_State_When_There_Are_No_Runs()
    {
        Services.AddScoped<IPipelineRunQueries>(_ => new FakePipelineRunQueries(Array.Empty<PipelineRunLogDto>()));

        var cut = Render<Treatments>();

        cut.Find("h1").TextContent.Should().Contain("Traitements");
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='traitements-empty']").Should().ContainSingle());
    }

    [Fact]
    public void Renders_The_Journal_With_French_Labels_Counters_And_Badges()
    {
        var started = new DateTimeOffset(2026, 6, 8, 2, 0, 0, TimeSpan.Zero);
        var runs = new[]
        {
            new PipelineRunLogDto
            {
                Id = Guid.NewGuid(),
                RunType = PipelineRunType.Check,
                Trigger = PipelineRunTrigger.Scheduled,
                StartedAt = started,
                CompletedAt = started.AddMinutes(1),
                DocumentsProcessed = 7,
                DocumentsSucceeded = 7,
                DocumentsFailed = 0,
                Detail = null,
            },
            new PipelineRunLogDto
            {
                Id = Guid.NewGuid(),
                RunType = PipelineRunType.Send,
                Trigger = PipelineRunTrigger.Manual,
                StartedAt = started.AddMinutes(5),
                CompletedAt = started.AddMinutes(7),
                DocumentsProcessed = 4,
                DocumentsSucceeded = 3,
                DocumentsFailed = 1,
                Detail = "rejetés: 1",
            },
        };

        Services.AddScoped<IPipelineRunQueries>(_ => new FakePipelineRunQueries(runs));

        var cut = Render<Treatments>();

        cut.WaitForAssertion(() =>
        {
            // No empty state when runs exist.
            cut.FindAll("[data-testid='traitements-empty']").Should().BeEmpty();

            // French nature + trigger labels rendered as badges (déclencheurs / nature).
            cut.FindAll("[data-testid='run-nature']").Should().HaveCount(2);
            cut.FindAll("[data-testid='run-trigger']").Should().HaveCount(2);
            cut.FindAll("[data-testid='run-failed']").Should().HaveCount(2);

            var markup = cut.Markup;
            markup.Should().Contain("Contrôle");
            markup.Should().Contain("Envoi");
            markup.Should().Contain("Manuel");
            markup.Should().Contain("Planifié");

            // Counters surfaced verbatim and the date formatted the French way (dd/MM/yyyy).
            markup.Should().Contain("08/06/2026");
            markup.Should().Contain("1 min 0 s");
        });
    }

    private sealed class FakePipelineRunQueries : IPipelineRunQueries
    {
        private readonly IReadOnlyList<PipelineRunLogDto> _runs;

        public FakePipelineRunQueries(IReadOnlyList<PipelineRunLogDto> runs) => _runs = runs;

        public Task<IReadOnlyList<PipelineRunLogDto>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult(_runs);

        public Task<IReadOnlyList<PipelineRunLogDto>> GetRunsAsync(
            DateOnly? fromInclusive,
            DateOnly? toInclusive,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_runs);
    }

    private sealed class FakeActorContextAccessor : IActorContextAccessor
    {
        public IActorContext Current { get; } = new AnonymousActorContext();

        private sealed class AnonymousActorContext : IActorContext
        {
            public Guid UserId => Guid.Empty;

            public Guid CorrelationId { get; } = Guid.NewGuid();

            public bool IsAuthenticated => false;

            public string? DisplayName => null;

            public string? Email => null;

            public Guid? CompanyId => null;

            public string? Timezone => null;

            public string? Language => null;

            public string? TenantId => null;
        }
    }

    private sealed class NoOpGridPreferenceService : IGridPreferenceService
    {
        public Task<UserGridPreference?> GetPreferenceAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
            Task.FromResult<UserGridPreference?>(null);

        public Task SavePreferenceAsync(Guid userId, string gridKey, IReadOnlyList<string> columnKeys, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveViewPreferenceAsync(Guid userId, string gridKey, ViewKind viewKind, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveFilterStateAsync(Guid userId, string gridKey, string? filterStateJson, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveColumnWidthsAsync(Guid userId, string gridKey, IReadOnlyDictionary<string, string> columnWidths, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpSavedFilterService : ISavedFilterService
    {
        public Task<IReadOnlyList<SavedFilter>> ListAsync(Guid userId, string gridKey, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SavedFilter>>(Array.Empty<SavedFilter>());

        public Task<SavedFilter?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<SavedFilter?>(null);

        public Task<SavedFilter> SaveAsync(SavedFilter filter, CancellationToken ct = default) =>
            Task.FromResult(filter);

        public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SetDefaultAsync(Guid id, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
