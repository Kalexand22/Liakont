namespace Liakont.Modules.Supervision.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Liakont.Modules.Supervision.Infrastructure;
using Stratum.Modules.Job.Contracts.DTOs;
using Stratum.Modules.Job.Contracts.Queries;
using Xunit;

/// <summary>
/// Lecture de la dernière évaluation du dead-man's-switch (FIX210, F12 §5.1) : interroge le module Job par le
/// type EXACT du job d'évaluation (filtré en SQL, pas de scan plafonné) et restitue son dernier achèvement.
/// </summary>
public sealed class SupervisionLivenessQueriesTests
{
    private static readonly string EvaluationJobType = typeof(SupervisionEvaluationTrigger).FullName!;

    [Fact]
    public async Task Queries_The_Job_Module_By_The_Supervision_Evaluation_Type()
    {
        var completedAt = new DateTimeOffset(2026, 6, 11, 11, 45, 0, TimeSpan.Zero);
        var jobQueries = new RecordingJobQueries(completedAt);
        var sut = new SupervisionLivenessQueries(jobQueries);

        var result = await sut.GetLastEvaluationUtcAsync();

        result.Should().Be(completedAt);
        jobQueries.LastRequestedType.Should().Be(EvaluationJobType);
    }

    [Fact]
    public async Task Returns_Null_When_The_Job_Was_Never_Completed()
    {
        var sut = new SupervisionLivenessQueries(new RecordingJobQueries(null));

        (await sut.GetLastEvaluationUtcAsync()).Should().BeNull();
    }

    private sealed class RecordingJobQueries : IJobQueries
    {
        private readonly DateTimeOffset? _lastCompletedAt;

        public RecordingJobQueries(DateTimeOffset? lastCompletedAt) => _lastCompletedAt = lastCompletedAt;

        public string? LastRequestedType { get; private set; }

        public Task<DateTimeOffset?> GetLastCompletedAtByTypeAsync(string jobType, CancellationToken ct = default)
        {
            LastRequestedType = jobType;
            return Task.FromResult(_lastCompletedAt);
        }

        public Task<JobDto?> GetByIdAsync(Guid jobId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<JobDto>> ListByStatusAsync(string status, int limit = 50, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> HasPendingJobOfTypeAsync(string jobType, Guid? companyId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
