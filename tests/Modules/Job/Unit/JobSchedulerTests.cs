namespace Stratum.Modules.Job.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stratum.Common.Abstractions.Events;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Domain.Entities;
using Stratum.Modules.Job.Infrastructure;
using Xunit;

/// <summary>
/// RDL08 / A6-scale-2 : exerce la branche de SUPPRESSION du <see cref="JobScheduler"/> (dé-duplication à
/// l'enqueue) DANS <c>ProcessDueSchedulesAsync</c> — le câblage lui-même, pas seulement la garde/la requête en
/// isolation. Prouve que, sur un hit `Pending`, le scheduler (a) n'insère AUCUN job et (b) avance tout de même
/// <c>next_run_at</c> ; et que sans garde le comportement d'origine est préservé.
/// </summary>
public sealed class JobSchedulerTests
{
    [Fact]
    public async Task ProcessDueSchedules_Suppresses_Enqueue_When_Guard_Reports_Pending_Duplicate()
    {
        var scheduleUow = new FakeScheduleUnitOfWork(DueSchedule());
        var jobUow = new FakeJobUnitOfWork();
        var guard = new FakeEnqueueGuard(suppress: true);

        await InvokeProcessDueSchedulesAsync(scheduleUow, jobUow, guard);

        jobUow.InsertedJobs.Should().BeEmpty("un doublon Pending → AUCUN job inséré (cœur de l'anti-empilement)");
        scheduleUow.UpdatedSchedules.Should().ContainSingle("next_run_at est tout de même avancé pour respecter la cadence");
        scheduleUow.UpdatedSchedules[0].NextRunAt.Should().BeAfter(DateTimeOffset.UtcNow);
        guard.Calls.Should().ContainSingle().Which.Should().Be(("Liakont.Test.RDL08.FanOut", Guid.Empty));
    }

    [Fact]
    public async Task ProcessDueSchedules_Enqueues_When_Guard_Reports_No_Duplicate()
    {
        var scheduleUow = new FakeScheduleUnitOfWork(DueSchedule());
        var jobUow = new FakeJobUnitOfWork();
        var guard = new FakeEnqueueGuard(suppress: false);

        await InvokeProcessDueSchedulesAsync(scheduleUow, jobUow, guard);

        // Contrôle : sans doublon, le job est bien enqueué (le harness insérerait — donc l'absence en cas de
        // suppression prouve réellement le saut, pas un harness inopérant).
        jobUow.InsertedJobs.Should().ContainSingle();
        scheduleUow.UpdatedSchedules.Should().ContainSingle();
    }

    [Fact]
    public async Task ProcessDueSchedules_Without_Guard_Enqueues_As_Before()
    {
        // Garde absente (composition sans le câblage Host) → comportement d'origine préservé (pas de crash).
        var scheduleUow = new FakeScheduleUnitOfWork(DueSchedule());
        var jobUow = new FakeJobUnitOfWork();

        await InvokeProcessDueSchedulesAsync(scheduleUow, jobUow, guard: null);

        jobUow.InsertedJobs.Should().ContainSingle();
    }

    private static JobSchedule DueSchedule() => JobSchedule.Create(
        name: "rdl08-fanout",
        cronExpression: "*/15 * * * *",
        jobType: "Liakont.Test.RDL08.FanOut",
        payloadTemplate: "{}",
        companyId: Guid.Empty,
        nextRunAt: DateTimeOffset.UtcNow.AddMinutes(-1));

    private static async Task InvokeProcessDueSchedulesAsync(
        IScheduleUnitOfWork scheduleUow,
        IJobUnitOfWork jobUow,
        IRecurringJobEnqueueGuard? guard)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IScheduleUnitOfWorkFactory>(new FakeScheduleUowFactory(scheduleUow));
        services.AddSingleton<IJobUnitOfWorkFactory>(new FakeJobUowFactory(jobUow));
        if (guard is not null)
        {
            services.AddSingleton(guard);
        }

        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var scheduler = new JobScheduler(
            scopeFactory,
            Options.Create(new JobSchedulerOptions()),
            NullLogger<JobScheduler>.Instance);

        var method = typeof(JobScheduler).GetMethod(
            "ProcessDueSchedulesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(scheduler, [CancellationToken.None])!;
    }

    private sealed class FakeEnqueueGuard : IRecurringJobEnqueueGuard
    {
        private readonly bool _suppress;

        public FakeEnqueueGuard(bool suppress) => _suppress = suppress;

        public List<(string Type, Guid? CompanyId)> Calls { get; } = [];

        public Task<bool> ShouldSuppressEnqueueAsync(string jobType, Guid? companyId, CancellationToken cancellationToken = default)
        {
            Calls.Add((jobType, companyId));
            return Task.FromResult(_suppress);
        }
    }

    private sealed class FakeScheduleUowFactory : IScheduleUnitOfWorkFactory
    {
        private readonly IScheduleUnitOfWork _uow;

        public FakeScheduleUowFactory(IScheduleUnitOfWork uow) => _uow = uow;

        public Task<IScheduleUnitOfWork> BeginAsync(CancellationToken ct = default) => Task.FromResult(_uow);
    }

    private sealed class FakeScheduleUnitOfWork : IScheduleUnitOfWork
    {
        private readonly JobSchedule _due;

        public FakeScheduleUnitOfWork(JobSchedule due) => _due = due;

        public List<JobSchedule> UpdatedSchedules { get; } = [];

        public Task<IReadOnlyList<JobSchedule>> GetDueSchedulesAsync(DateTimeOffset now, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<JobSchedule>>([_due]);

        public Task UpdateScheduleAsync(JobSchedule schedule, CancellationToken ct = default)
        {
            UpdatedSchedules.Add(schedule);
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task InsertScheduleAsync(JobSchedule schedule, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<JobSchedule?> GetScheduleByIdAsync(Guid scheduleId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> ExistsByNameAndCompanyAsync(string name, Guid companyId, Guid? excludeId = null, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetActiveJobTypesAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeJobUowFactory : IJobUnitOfWorkFactory
    {
        private readonly IJobUnitOfWork _uow;

        public FakeJobUowFactory(IJobUnitOfWork uow) => _uow = uow;

        public Task<IJobUnitOfWork> BeginAsync(CancellationToken ct = default) => Task.FromResult(_uow);
    }

    private sealed class FakeJobUnitOfWork : IJobUnitOfWork
    {
        public List<JobEntry> InsertedJobs { get; } = [];

        public Task InsertJobAsync(JobEntry job, CancellationToken ct = default)
        {
            InsertedJobs.Add(job);
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateJobAsync(JobEntry job, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<JobEntry?> GetJobByIdAsync(Guid jobId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<JobEntry?> AcquireNextPendingJobAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task CommitWithEventAsync<TPayload>(IntegrationEvent<TPayload> integrationEvent, CancellationToken ct = default) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
