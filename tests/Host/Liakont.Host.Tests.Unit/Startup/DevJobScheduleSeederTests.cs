namespace Liakont.Host.Tests.Unit.Startup;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Host.Startup;
using MediatR;
using Microsoft.Extensions.Logging;
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

        await DevJobScheduleSeeder.TrySeedScheduleAsync(sender, Sample, Company, new NullTestLogger());

        sender.Sent.Should().ContainSingle(r => r is CreateScheduleCommand);
        var cmd = (CreateScheduleCommand)sender.Sent[0];
        cmd.Name.Should().Be(Sample.ScheduleName);
        cmd.CronExpression.Should().Be(Sample.CronExpression);
        cmd.JobType.Should().Be(Sample.JobType);
        cmd.CompanyId.Should().Be(Company);
    }

    [Fact]
    public async Task TrySeedSchedule_Should_Swallow_AlreadyExists_So_Boot_Is_Idempotent()
    {
        var sender = new FakeSender
        {
            ThrowOn = _ => new InvalidOperationException("INV-JOB-005: A schedule named 'x' already exists for this company."),
        };

        var act = async () => await DevJobScheduleSeeder.TrySeedScheduleAsync(sender, Sample, Company, new NullTestLogger());

        await act.Should().NotThrowAsync("create-only : un re-boot ne doit pas planter sur un schedule déjà présent");
    }

    [Fact]
    public async Task TrySeedSchedule_Should_Swallow_Unexpected_Errors()
    {
        var sender = new FakeSender { ThrowOn = _ => new InvalidOperationException("base indisponible") };

        var act = async () => await DevJobScheduleSeeder.TrySeedScheduleAsync(sender, Sample, Company, new NullTestLogger());

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
