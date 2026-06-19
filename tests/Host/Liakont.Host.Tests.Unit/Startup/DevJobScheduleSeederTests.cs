namespace Liakont.Host.Tests.Unit.Startup;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Startup;
using MediatR;
using Microsoft.Extensions.Logging;
using Stratum.Modules.Job.Application;
using Stratum.Modules.Job.Contracts.Commands;
using Xunit;

/// <summary>
/// FIX203b : l'amorçage DEV des planifications système est CREATE-ONLY et best-effort — il envoie une
/// <see cref="CreateScheduleCommand"/> bien formée, n'écrase pas un schedule existant (INV-JOB-005) et
/// ne fait jamais planter le démarrage.
/// </summary>
public sealed class DevJobScheduleSeederTests
{
    private static readonly SystemJobDefinition Sample =
        new("Liakont.Modules.Supervision.Infrastructure.SupervisionEvaluationTrigger", "Évaluation de la supervision", "*/15 * * * *", "label");

    private static readonly Guid Company = Guid.Parse("ad000000-0000-4000-b000-000000000001");

    [Fact]
    public async Task TrySeedSchedule_Should_Send_CreateScheduleCommand_With_Definition_Values()
    {
        var sender = new FakeSender();
        var uowFactory = new FakeScheduleUowFactory { Exists = false };

        await DevJobScheduleSeeder.TrySeedScheduleAsync(sender, uowFactory, Sample, Company, new NullTestLogger());

        sender.Sent.Should().ContainSingle(r => r is CreateScheduleCommand);
        var cmd = (CreateScheduleCommand)sender.Sent[0];
        cmd.Name.Should().Be(Sample.ScheduleName);
        cmd.CronExpression.Should().Be(Sample.CronExpression);
        cmd.JobType.Should().Be(Sample.JobType);
        cmd.CompanyId.Should().Be(Company);
    }

    [Fact]
    public async Task TrySeedSchedule_Should_Not_Seed_DeploymentCadence_Job_Without_Sourced_Cron()
    {
        // RDL07/A6-cons-2 : un job à cadence de déploiement (cron null) n'est jamais amorcé en dev — sa
        // planification reste un geste opérateur ; aucune cadence n'est inventée.
        var deploymentCadence = new SystemJobDefinition(
            "Liakont.Modules.Pipeline.Contracts.Jobs.SyncAllTrigger",
            "Synchronisation des comptes rendus (tous les tenants)",
            null,
            "label",
            SystemJobClass.DeploymentCadence);
        var sender = new FakeSender();
        var uowFactory = new FakeScheduleUowFactory { Exists = false };

        await DevJobScheduleSeeder.TrySeedScheduleAsync(sender, uowFactory, deploymentCadence, Company, new NullTestLogger());

        sender.Sent.Should().BeEmpty("un job sans cron sourcé n'est pas amorçable");
    }

    [Fact]
    public async Task TrySeedSchedule_Should_Swallow_AlreadyExists_So_Boot_Is_Idempotent()
    {
        var sender = new FakeSender();
        var uowFactory = new FakeScheduleUowFactory { Exists = true };

        var act = async () => await DevJobScheduleSeeder.TrySeedScheduleAsync(sender, uowFactory, Sample, Company, new NullTestLogger());

        await act.Should().NotThrowAsync("create-only : un re-boot ne doit pas planter sur un schedule déjà présent");
        sender.Sent.OfType<CreateScheduleCommand>().Should().BeEmpty("schedule déjà présent — pas de création");
    }

    [Fact]
    public async Task TrySeedSchedule_Should_Swallow_Unexpected_Errors()
    {
        var sender = new FakeSender { ThrowOn = _ => new InvalidOperationException("base indisponible") };
        var uowFactory = new FakeScheduleUowFactory { Exists = false };

        var act = async () => await DevJobScheduleSeeder.TrySeedScheduleAsync(sender, uowFactory, Sample, Company, new NullTestLogger());

        await act.Should().NotThrowAsync("le seed de dev ne doit jamais faire planter le démarrage");
    }

    private sealed class FakeSender : ISender
    {
        public List<object> Sent { get; } = [];

        public Guid ScheduleId { get; init; } = Guid.NewGuid();

        public Func<object, Exception?>? ThrowOn { get; init; }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Sent.Add(request!);
            if (ThrowOn?.Invoke(request!) is { } ex)
            {
                throw ex;
            }

            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            if (ThrowOn?.Invoke(request) is { } ex)
            {
                throw ex;
            }

            return Task.FromResult((TResponse)(object)ScheduleId);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeScheduleUowFactory : IScheduleUnitOfWorkFactory
    {
        public bool Exists { get; init; }

        public Task<IScheduleUnitOfWork> BeginAsync(CancellationToken ct = default) =>
            Task.FromResult<IScheduleUnitOfWork>(new FakeScheduleUow { Exists = Exists });
    }

    private sealed class FakeScheduleUow : IScheduleUnitOfWork
    {
        public bool Exists { get; init; }

        public Task<bool> ExistsByNameAndCompanyAsync(string name, Guid companyId, Guid? excludeId = null, CancellationToken ct = default) =>
            Task.FromResult(Exists);

        public Task InsertScheduleAsync(Stratum.Modules.Job.Domain.Entities.JobSchedule schedule, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UpdateScheduleAsync(Stratum.Modules.Job.Domain.Entities.JobSchedule schedule, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Stratum.Modules.Job.Domain.Entities.JobSchedule?> GetScheduleByIdAsync(Guid scheduleId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Stratum.Modules.Job.Domain.Entities.JobSchedule>> GetDueSchedulesAsync(DateTimeOffset now, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetActiveJobTypesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullTestLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
