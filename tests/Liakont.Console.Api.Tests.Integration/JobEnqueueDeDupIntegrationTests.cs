namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Stratum.Modules.Job.Contracts.Queries;
using Xunit;

/// <summary>
/// RDL08 / A6-scale-2 : la requête de dé-duplication à l'enqueue (<see cref="IJobQueries.HasPendingJobOfTypeAsync"/>)
/// contre la VRAIE table système <c>job.jobs</c>. Détecte un job EN ATTENTE (Pending) du même type et de la même
/// portée tenant, ce que le scheduler récurrent consulte avant d'enqueuer (anti-empilement). Pending-only :
/// un job <c>Running</c> n'est jamais compté (anti-deadlock sur un Running orphelin, ADR-0006 §5).
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class JobEnqueueDeDupIntegrationTests
{
    // Types FICTIFS, jamais des types de jobs réels — pour ne pas faire dispatcher le worker dessus.
    private const string PendingProbeType = "Liakont.Test.RDL08.DeDupPendingProbe";
    private const string RunningProbeType = "Liakont.Test.RDL08.DeDupRunningProbe";

    private readonly ConsoleApiFactory _factory;

    public JobEnqueueDeDupIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HasPendingJobOfType_Detects_Pending_System_Job_Honouring_Scope_And_Status()
    {
        await CleanupAsync();

        // Le scheduler récurrent insère/interroge TOUJOURS avec `schedule.CompanyId` — un GUID NON-NUL (la
        // portée tenant du schedule). C'est le chemin réellement emprunté en production : on le valide en
        // priorité, le cas NULL n'étant qu'un garde-fou défensif (jamais produit par le scheduler).
        var scheduleScope = Guid.NewGuid();
        var otherScope = Guid.NewGuid();
        try
        {
            // Aucun job du type sondé → pas de suppression.
            (await QueryHasPendingAsync(PendingProbeType, scheduleScope)).Should().BeFalse();

            // Un job EN ATTENTE (Pending) du même type ET de la même portée (GUID non-nul) existe.
            await InsertJobAsync(PendingProbeType, status: "Pending", companyId: scheduleScope);

            // Même type + même portée (GUID) → suppression : le chemin nominal de production.
            (await QueryHasPendingAsync(PendingProbeType, scheduleScope)).Should().BeTrue();

            // Même type, portée tenant DIFFÉRENTE → PAS de suppression (la dé-dup est tenant-scopée).
            (await QueryHasPendingAsync(PendingProbeType, otherScope)).Should().BeFalse();

            // Portée NULL (cas-limite défensif, non produit par le scheduler) → pas de suppression non plus.
            (await QueryHasPendingAsync(PendingProbeType, companyId: null)).Should().BeFalse();

            // Un job RUNNING (orphelin potentiel) n'est JAMAIS compté : Pending-only, anti-deadlock (A6-scale-1).
            await InsertJobAsync(RunningProbeType, status: "Running", companyId: scheduleScope);
            (await QueryHasPendingAsync(RunningProbeType, scheduleScope)).Should().BeFalse();
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task<bool> QueryHasPendingAsync(string jobType, Guid? companyId)
    {
        // Sans contexte de tenant établi, l'IConnectionFactory route vers la base SYSTÈME — exactement comme le
        // JobScheduler récurrent qui consulte la garde dans son propre scope sans tenant.
        using var scope = _factory.Services.CreateScope();
        var queries = scope.ServiceProvider.GetRequiredService<IJobQueries>();
        return await queries.HasPendingJobOfTypeAsync(jobType, companyId);
    }

    private async Task InsertJobAsync(string type, string status, Guid? companyId)
    {
        await using var conn = new NpgsqlConnection(_factory.SystemConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO job.jobs (type, payload, status, started_at, company_id)
            VALUES (@Type, '{}'::jsonb, @Status, CASE WHEN @Status = 'Running' THEN now() ELSE NULL END, @CompanyId)
            """,
            new { Type = type, Status = status, CompanyId = companyId });
    }

    private async Task CleanupAsync()
    {
        await using var conn = new NpgsqlConnection(_factory.SystemConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "DELETE FROM job.jobs WHERE type = ANY(@Types)",
            new { Types = new[] { PendingProbeType, RunningProbeType } });
    }
}
