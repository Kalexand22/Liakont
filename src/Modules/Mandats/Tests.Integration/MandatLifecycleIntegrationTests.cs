namespace Liakont.Modules.Mandats.Tests.Integration;

using Dapper;
using FluentAssertions;
using Liakont.Modules.Mandats.Domain.Entities;
using Liakont.Modules.Mandats.Infrastructure;
using Liakont.Modules.Mandats.Tests.Integration.Fixtures;
using Npgsql;
using Xunit;

/// <summary>
/// Cycle de vie des mandats (F15 §1.5/§2.2) sur PostgreSQL réel (Testcontainers) : round-trip et
/// suspendu-par-défaut persisté (INV-MANDATS-4), invalidation + journalisation atomique (INV-MANDATS-3/6),
/// journal append-only (UPDATE/DELETE/TRUNCATE rejetés par trigger base), isolation tenant.
/// </summary>
[Collection("MandatsIntegration")]
public sealed class MandatLifecycleIntegrationTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromDays(30);

    private readonly MandatsDatabaseFixture _fixture;

    public MandatLifecycleIntegrationTests(MandatsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Insert_And_Get_RoundTrips_Including_Null_Status_And_Delay()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var mandantId = await SeedMandantAsync(harness, companyId);

        var mandat = Mandat.Create(companyId, mandantId, "MDT-1", "Clause d'exemple", estEcrit: false, assujettissementStatus: null, contestationDelay: null);
        await InsertMandatAsync(harness, mandat, Guid.NewGuid());

        var dto = await harness.Queries.GetMandat(companyId, mandantId, "MDT-1");
        dto.Should().NotBeNull();
        dto!.ClauseText.Should().Be("Clause d'exemple");
        dto.EstEcrit.Should().BeFalse();
        dto.AssujettissementStatus.Should().BeNull();
        dto.ContestationDelay.Should().BeNull();
        dto.IsValidated.Should().BeFalse();
        dto.IsSelfBillingSuspended.Should().BeTrue("statut et délai null ⇒ 389 suspendu (INV-MANDATS-4).");
    }

    [Fact]
    public async Task Validated_Mandat_With_Status_And_Delay_Is_Not_Suspended_After_RoundTrip()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var mandantId = await SeedMandantAsync(harness, companyId);

        var mandat = Mandat.Create(companyId, mandantId, "MDT-1", "Clause", estEcrit: true, assujettissementStatus: "ASSUJETTI", contestationDelay: Delay);
        await InsertMandatAsync(harness, mandat, Guid.NewGuid());

        // Validation via le chemin de mutation atomique.
        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            var loaded = await uow.GetMandatForUpdateAsync(companyId, mandantId, "MDT-1");
            var previousBy = loaded!.ValidatedBy;
            var previousDate = loaded.ValidatedDate;
            loaded.Validate("Valideur");
            var entry = MandatChangeLogFactory.ForValidateMandat(loaded, previousBy, previousDate, Guid.NewGuid(), "Opérateur de test");
            await uow.SaveMandatMutationAsync(loaded, entry);
            await uow.CommitAsync();
        }

        var dto = await harness.Queries.GetMandat(companyId, mandantId, "MDT-1");
        dto!.IsValidated.Should().BeTrue();
        dto.ContestationDelay.Should().Be(Delay, "le délai (interval) doit faire un round-trip exact.");
        dto.IsSelfBillingSuspended.Should().BeFalse("statut + délai + validé + non révoqué ⇒ 389 actif.");
    }

    [Fact]
    public async Task UpdateTerms_Invalidates_And_Logs_UpdateMandat()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var mandantId = await SeedMandantAsync(harness, companyId);
        var mandat = Mandat.Create(companyId, mandantId, "MDT-1", "Clause", estEcrit: true, assujettissementStatus: "ASSUJETTI", contestationDelay: Delay);
        await InsertMandatAsync(harness, mandat, Guid.NewGuid());

        // Valider d'abord, puis muter : la mutation doit repasser « NON VALIDÉE » (INV-MANDATS-6).
        await MutateAsync(harness, companyId, mandantId, "MDT-1", m => m.Validate("Valideur"), MandatChangeType.ValidateMandat);

        await MutateAsync(
            harness, companyId, mandantId, "MDT-1",
            m => m.UpdateTerms("Clause modifiée", estEcrit: false, assujettissementStatus: "ASSUJETTI", contestationDelay: Delay),
            MandatChangeType.UpdateMandat);

        var dto = await harness.Queries.GetMandat(companyId, mandantId, "MDT-1");
        dto!.ClauseText.Should().Be("Clause modifiée");
        dto.IsValidated.Should().BeFalse("toute mutation repasse « NON VALIDÉE ».");

        // CreateMandant + CreateMandat + ValidateMandat + UpdateMandat (journal lu en ordre décroissant).
        var log = await harness.Queries.GetChangeLog(companyId);
        log.Should().HaveCount(4);
        log[0].ChangeType.Should().Be(nameof(MandatChangeType.UpdateMandat));
        log[0].MandatId.Should().NotBeNull();
    }

    [Fact]
    public async Task ChangeLog_Is_Append_Only_Update_And_Delete_Rejected()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        await SeedMandantAsync(harness, companyId);

        using var conn = await harness.ConnectionFactory.OpenAsync();

        var update = async () => await conn.ExecuteAsync(
            "UPDATE mandats.mandat_change_log SET operator_name = 'falsifié' WHERE company_id = @c",
            new { c = companyId });
        var delete = async () => await conn.ExecuteAsync(
            "DELETE FROM mandats.mandat_change_log WHERE company_id = @c",
            new { c = companyId });

        (await update.Should().ThrowAsync<PostgresException>())
            .Which.MessageText.Should().Contain("append-only");
        await delete.Should().ThrowAsync<PostgresException>();

        (await ChangeLogCountAsync(harness, companyId)).Should().Be(1, "ni l'UPDATE ni le DELETE n'ont abouti.");
    }

    [Fact]
    public async Task ChangeLog_Truncate_Is_Rejected()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        await SeedMandantAsync(harness, companyId);

        using var conn = await harness.ConnectionFactory.OpenAsync();
        var truncate = async () => await conn.ExecuteAsync("TRUNCATE mandats.mandat_change_log");

        await truncate.Should().ThrowAsync<PostgresException>();
        (await ChangeLogCountAsync(harness, companyId)).Should().Be(1, "le TRUNCATE a été rejeté.");
    }

    [Fact]
    public async Task Mutation_And_ChangeLog_Are_Atomic_Abandoned_Transaction_Persists_Nothing()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var mandantId = await SeedMandantAsync(harness, companyId);
        var mandat = Mandat.Create(companyId, mandantId, "MDT-1", "Clause", estEcrit: true, assujettissementStatus: "ASSUJETTI", contestationDelay: Delay);
        await InsertMandatAsync(harness, mandat, Guid.NewGuid());

        var logBefore = await ChangeLogCountAsync(harness, companyId);

        await using (var uow = await harness.UowFactory.BeginAsync())
        {
            var loaded = await uow.GetMandatForUpdateAsync(companyId, mandantId, "MDT-1");
            loaded!.UpdateTerms("Clause abandonnée", estEcrit: false, assujettissementStatus: null, contestationDelay: null);
            var entry = MandatChangeLogFactory.ForUpdateMandat(mandat, loaded, Guid.NewGuid(), "Opérateur de test");
            await uow.SaveMandatMutationAsync(loaded, entry);

            // Pas de CommitAsync : la sortie du bloc déclenche le rollback (TransactionScope.DisposeAsync).
        }

        var dto = await harness.Queries.GetMandat(companyId, mandantId, "MDT-1");
        dto!.ClauseText.Should().Be("Clause", "la mutation non validée a été annulée.");
        (await ChangeLogCountAsync(harness, companyId)).Should().Be(logBefore, "l'entrée de journal a été annulée avec la mutation.");
    }

    [Fact]
    public async Task Tenant_Isolation_Mutation_Does_Not_Touch_Other_Tenant()
    {
        var harness = new MandatsHarness(_fixture);
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var mandantA = await SeedMandantAsync(harness, companyA);
        var mandantB = await SeedMandantAsync(harness, companyB);

        await InsertMandatAsync(harness, Mandat.Create(companyA, mandantA, "MDT-1", "Clause A", true, "ASSUJETTI", Delay), Guid.NewGuid());
        await InsertMandatAsync(harness, Mandat.Create(companyB, mandantB, "MDT-1", "Clause B", true, "ASSUJETTI", Delay), Guid.NewGuid());

        await MutateAsync(
            harness, companyA, mandantA, "MDT-1",
            m => m.UpdateTerms("Clause A modifiée", true, "ASSUJETTI", Delay),
            MandatChangeType.UpdateMandat);

        (await harness.Queries.GetMandat(companyA, mandantA, "MDT-1"))!.ClauseText.Should().Be("Clause A modifiée");
        (await harness.Queries.GetMandat(companyB, mandantB, "MDT-1"))!.ClauseText.Should().Be("Clause B", "la mutation sur A ne touche pas B (CLAUDE.md n°9).");

        // B n'a que ses deux créations (CreateMandant + CreateMandat) ; la mutation UpdateMandat de A
        // n'a écrit aucune ligne dans le journal de B (isolation tenant du journal append-only).
        (await ChangeLogCountAsync(harness, companyB)).Should().Be(2);
        (await harness.Queries.GetChangeLog(companyB))
            .Should().NotContain(e => e.ChangeType == nameof(MandatChangeType.UpdateMandat),
                "aucune mutation de A n'apparaît dans le journal de B.");
    }

    [Fact]
    public async Task Revoke_Persists_And_Logs_And_Suspends_After_RoundTrip()
    {
        var harness = new MandatsHarness(_fixture);
        var companyId = Guid.NewGuid();
        var mandantId = await SeedMandantAsync(harness, companyId);
        var mandat = Mandat.Create(companyId, mandantId, "MDT-1", "Clause", estEcrit: true, assujettissementStatus: "ASSUJETTI", contestationDelay: Delay);
        await InsertMandatAsync(harness, mandat, Guid.NewGuid());

        // Valider (389 actif) puis révoquer : la révocation persistée doit suspendre le 389 après relecture.
        await MutateAsync(harness, companyId, mandantId, "MDT-1", m => m.Validate("Valideur"), MandatChangeType.ValidateMandat);
        await MutateAsync(harness, companyId, mandantId, "MDT-1", m => m.Revoke(), MandatChangeType.RevokeMandat);

        var dto = await harness.Queries.GetMandat(companyId, mandantId, "MDT-1");
        dto!.IsRevoked.Should().BeTrue();
        dto.RevokedDate.Should().NotBeNull("revoked_date (timestamptz) doit faire un round-trip.");
        dto.IsSelfBillingSuspended.Should().BeTrue("un mandat révoqué a 389 suspendu (INV-MANDATS-4).");

        var log = await harness.Queries.GetChangeLog(companyId);
        log[0].ChangeType.Should().Be(nameof(MandatChangeType.RevokeMandat));
        log[0].MandatId.Should().NotBeNull();
        log[0].AfterValue.Should().NotBeNull();
    }

    private static async Task<Guid> SeedMandantAsync(MandatsHarness harness, Guid companyId)
    {
        var mandant = Mandant.Create(companyId, "MANDANT-EXEMPLE-1", "Ferme Exemple", null, "000000000", "EXM-");
        await using var uow = await harness.UowFactory.BeginAsync();
        var entry = MandatChangeLogFactory.ForCreateMandant(mandant, Guid.NewGuid(), "Opérateur de test");
        await uow.InsertMandantAsync(mandant, entry);
        await uow.CommitAsync();
        return mandant.Id;
    }

    private static async Task InsertMandatAsync(MandatsHarness harness, Mandat mandat, Guid operatorId)
    {
        await using var uow = await harness.UowFactory.BeginAsync();
        var entry = MandatChangeLogFactory.ForCreateMandat(mandat, operatorId, "Opérateur de test");
        await uow.InsertMandatAsync(mandat, entry);
        await uow.CommitAsync();
    }

    private static async Task MutateAsync(
        MandatsHarness harness, Guid companyId, Guid mandantId, string reference, Action<Mandat> mutate, MandatChangeType kind)
    {
        await using var uow = await harness.UowFactory.BeginAsync();

        // Deux instances distinctes du même agrégat : « before » reste l'état d'origine (jamais muté),
        // « loaded » porte la mutation — le journal UpdateMandat compare l'un à l'autre. Le second FOR UPDATE
        // sur une ligne déjà verrouillée par cette transaction est ré-entrant (aucun blocage).
        var before = await uow.GetMandatForUpdateAsync(companyId, mandantId, reference);
        var loaded = await uow.GetMandatForUpdateAsync(companyId, mandantId, reference);
        var previousBy = loaded!.ValidatedBy;
        var previousDate = loaded.ValidatedDate;
        mutate(loaded);
        var entry = kind switch
        {
            MandatChangeType.ValidateMandat => MandatChangeLogFactory.ForValidateMandat(loaded, previousBy, previousDate, Guid.NewGuid(), "Opérateur de test"),
            MandatChangeType.RevokeMandat => MandatChangeLogFactory.ForRevokeMandat(loaded, Guid.NewGuid(), "Opérateur de test"),
            _ => MandatChangeLogFactory.ForUpdateMandat(before!, loaded, Guid.NewGuid(), "Opérateur de test"),
        };
        await uow.SaveMandatMutationAsync(loaded, entry);
        await uow.CommitAsync();
    }

    private static async Task<int> ChangeLogCountAsync(MandatsHarness harness, Guid companyId)
    {
        using var conn = await harness.ConnectionFactory.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM mandats.mandat_change_log WHERE company_id = @c",
            new { c = companyId });
    }
}
