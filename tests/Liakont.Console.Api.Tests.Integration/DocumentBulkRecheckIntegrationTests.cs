namespace Liakont.Console.Api.Tests.Integration;

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

/// <summary>
/// Tests d'intégration in-process de la re-vérification EN MASSE (FIX207, <c>IDocumentRecheckService.RecheckManyAsync</c>) :
/// boucle la re-vérification unitaire sur plusieurs documents bloqués du tenant, agrège les compteurs, et — point
/// vérifié ici sur une base RÉELLE — inscrit la trace d'audit append-only (FIX02) PAR document, attribuée à
/// l'opérateur (jamais un déblocage de masse anonyme). Opère sur le tenant dédié <see cref="ConsoleApiFactory.TenantVerdict"/>
/// (profil + table TVA validée), comme les actions unitaires verdict/recheck, et seede ses propres documents.
/// </summary>
[Collection(ConsoleApiCollectionFixture.Name)]
public sealed class DocumentBulkRecheckIntegrationTests
{
    private readonly ConsoleApiFactory _factory;

    public DocumentBulkRecheckIntegrationTests(ConsoleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RecheckMany_Rechecks_Each_Blocked_Document_And_Audits_Per_Document()
    {
        // Deux documents bloqués (acheteur à indice « société ») dans le tenant d'action. Le premier reçoit le verdict
        // B2C → il se débloquera à la re-vérification ; le second reste bloqué (garde-fou inchangé, sans verdict).
        var unblockable = await _factory.SeedBlockedProfessionalBuyerDocumentAsync(ConsoleApiFactory.TenantVerdict);
        var staysBlocked = await _factory.SeedBlockedProfessionalBuyerDocumentAsync(ConsoleApiFactory.TenantVerdict);
        await _factory.ConfirmBuyerB2cInScopeAsync(ConsoleApiFactory.TenantVerdict, unblockable);

        var summary = await _factory.RecheckManyInScopeAsync(
            ConsoleApiFactory.TenantVerdict, new[] { unblockable, staysBlocked });

        // Compteurs agrégés : 1 débloqué, 1 resté bloqué, sur 2 re-vérifiés.
        summary.Total.Should().Be(2);
        summary.Unblocked.Should().Be(1);
        summary.StillBlocked.Should().Be(1);
        summary.Unavailable.Should().Be(0);
        summary.Skipped.Should().Be(0);

        // Transitions réellement persistées.
        (await _factory.GetDocumentStateAsync(ConsoleApiFactory.TenantVerdict, unblockable)).Should().Be("ReadyToSend");
        (await _factory.GetDocumentStateAsync(ConsoleApiFactory.TenantVerdict, staysBlocked)).Should().Be("Blocked");

        // Trace d'audit FIX02 PAR DOCUMENT, attribuée à l'opérateur — la re-vérification de masse n'est pas anonyme.
        (await _factory.CountDocumentEventsAsync(ConsoleApiFactory.TenantVerdict, unblockable, "DocumentReadyToSend"))
            .Should().Be(1, "le déblocage par re-vérification de masse écrit un fait d'audit append-only");
        (await _factory.GetLatestEventOperatorIdentityAsync(ConsoleApiFactory.TenantVerdict, unblockable, "DocumentReadyToSend"))
            .Should().Be(ConsoleApiFactory.OperatorUserId.ToString(), "le déblocage de masse est attribué à l'opérateur (FIX02)");
        (await _factory.CountDocumentEventsAsync(ConsoleApiFactory.TenantVerdict, staysBlocked, "DocumentRecheckedStillBlocked"))
            .Should().Be(1, "un document resté bloqué inscrit aussi un fait d'audit (FIX02)");
        (await _factory.GetLatestEventOperatorIdentityAsync(ConsoleApiFactory.TenantVerdict, staysBlocked, "DocumentRecheckedStillBlocked"))
            .Should().Be(ConsoleApiFactory.OperatorUserId.ToString());
    }

    [Fact]
    public async Task RecheckMany_Deduplicates_Ids_And_Skips_Already_Changed_Documents()
    {
        // Robustesse : un doublon n'est re-vérifié et audité qu'une fois (Distinct), et un identifiant inexistant
        // (état déjà changé / hors tenant) est « ignoré » gracieusement — jamais une exception ni un faux audit.
        var blocked = await _factory.SeedBlockedProfessionalBuyerDocumentAsync(ConsoleApiFactory.TenantVerdict);
        var ghost = Guid.NewGuid();

        var summary = await _factory.RecheckManyInScopeAsync(
            ConsoleApiFactory.TenantVerdict, new[] { blocked, blocked, ghost });

        summary.Total.Should().Be(2, "le bloqué dédoublonné (une fois) + le fantôme");
        summary.StillBlocked.Should().Be(1);
        summary.Skipped.Should().Be(1, "l'identifiant inexistant est ignoré");
        (await _factory.CountDocumentEventsAsync(ConsoleApiFactory.TenantVerdict, blocked, "DocumentRecheckedStillBlocked"))
            .Should().Be(1, "malgré le doublon, un seul fait d'audit est inscrit pour le document");
    }
}
