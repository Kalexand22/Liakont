namespace Liakont.Host.Tests.Unit.Startup;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Startup;
using Stratum.Modules.Job.Contracts.DTOs;
using Stratum.Modules.Job.Contracts.Queries;
using Xunit;

/// <summary>
/// RDL08 / A6-scale-2 : la garde de dé-duplication à l'enqueue. Le scheduler récurrent ne doit pas empiler
/// un déclencheur identique quand un job du même type/portée est déjà EN ATTENTE (Pending). Pending-only :
/// un Running orphelin (crash, aucun reaper) ne doit JAMAIS bloquer le ré-enqueue (ADR-0006 §5).
/// </summary>
public sealed class RecurringJobEnqueueGuardTests
{
    [Fact]
    public async Task ShouldSuppressEnqueue_Returns_True_When_A_Pending_Job_Of_Same_Type_Exists()
    {
        var queries = new FakeJobQueries(hasPending: true);
        var guard = new RecurringJobEnqueueGuard(queries);

        var suppress = await guard.ShouldSuppressEnqueueAsync("Liakont.SomeFanOutJob", companyId: null);

        suppress.Should().BeTrue();
        queries.LastType.Should().Be("Liakont.SomeFanOutJob");
        queries.LastCompanyId.Should().BeNull();
    }

    [Fact]
    public async Task ShouldSuppressEnqueue_Returns_False_When_No_Pending_Job_Exists()
    {
        // Aucun Pending (file vide, ou le précédent est Running/Completed) → on enqueue normalement.
        var queries = new FakeJobQueries(hasPending: false);
        var guard = new RecurringJobEnqueueGuard(queries);

        var suppress = await guard.ShouldSuppressEnqueueAsync("Liakont.SomeFanOutJob", companyId: null);

        suppress.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldSuppressEnqueue_Passes_TenantScope_To_The_Query()
    {
        var company = Guid.NewGuid();
        var queries = new FakeJobQueries(hasPending: false);
        var guard = new RecurringJobEnqueueGuard(queries);

        await guard.ShouldSuppressEnqueueAsync("Liakont.TenantJob", companyId: company);

        queries.LastCompanyId.Should().Be(company);
    }

    [Fact]
    public async Task ShouldSuppressEnqueue_Throws_On_Blank_JobType()
    {
        var guard = new RecurringJobEnqueueGuard(new FakeJobQueries(hasPending: false));

        var act = async () => await guard.ShouldSuppressEnqueueAsync("  ", companyId: null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class FakeJobQueries : IJobQueries
    {
        private readonly bool _hasPending;

        public FakeJobQueries(bool hasPending) => _hasPending = hasPending;

        public string? LastType { get; private set; }

        public Guid? LastCompanyId { get; private set; }

        public Task<bool> HasPendingJobOfTypeAsync(string jobType, Guid? companyId, CancellationToken ct = default)
        {
            LastType = jobType;
            LastCompanyId = companyId;
            return Task.FromResult(_hasPending);
        }

        public Task<JobDto?> GetByIdAsync(Guid jobId, CancellationToken ct = default)
            => Task.FromResult<JobDto?>(null);

        public Task<IReadOnlyList<JobDto>> ListByStatusAsync(string status, int limit = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<JobDto>>([]);

        public Task<DateTimeOffset?> GetLastCompletedAtByTypeAsync(string jobType, CancellationToken ct = default)
            => Task.FromResult<DateTimeOffset?>(null);
    }
}
