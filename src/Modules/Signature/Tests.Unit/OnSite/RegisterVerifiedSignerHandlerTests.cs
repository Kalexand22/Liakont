namespace Liakont.Modules.Signature.Tests.Unit.OnSite;

using FluentAssertions;
using Liakont.Modules.Documents.Contracts.DTOs;
using Liakont.Modules.Signature.Application.OnSite;
using Liakont.Modules.Signature.Infrastructure.OnSite;
using Liakont.Modules.Signature.Tests.Unit.TestDoubles.OnSite;
using Stratum.Common.Abstractions.Exceptions;
using Xunit;

/// <summary>
/// Gardes de l'enregistrement du signataire vérifié (ADR-0030 §5 ; INV-ONSITE-7) : tenant-scoping serveur
/// (404 si le document n'appartient pas au tenant) et consignation de la liaison telle que fournie.
/// </summary>
public sealed class RegisterVerifiedSignerHandlerTests
{
    private static readonly Guid CompanyId = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid DocumentId = Guid.Parse("0a000003-0000-0000-0000-000000000003");
    private static readonly Guid OperatorId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static DocumentDto Document() => new()
    {
        Id = DocumentId,
        SourceReference = "src-1",
        DocumentNumber = "FA-A-001",
        DocumentType = "invoice",
        IssueDate = new DateOnly(2026, 1, 10),
        CustomerIsCompanyHint = false,
        TotalNet = 100m,
        TotalTax = 20m,
        TotalGross = 120m,
        State = "Issued",
        PayloadHash = "payload-hash",
        FirstSeenUtc = DateTimeOffset.UnixEpoch,
        LastUpdateUtc = DateTimeOffset.UnixEpoch,
    };

    private static RegisterVerifiedSignerCommand Command() => new()
    {
        CompanyId = CompanyId,
        RegisteredByUserId = OperatorId,
        DocumentId = DocumentId,
        SignerIdentity = "Mandant Réel (SVV)",
        VerificationMethod = "identification en personne par la SVV au guichet",
    };

    [Fact]
    public async Task Handle_UnknownDocument_ThrowsNotFound()
    {
        var handler = new RegisterVerifiedSignerHandler(
            new FakeDocumentQueries(document: null), new FakeOnSiteSignerBindingStore(resolved: null));

        await FluentActions.Awaiting(() => handler.Handle(Command(), CancellationToken.None))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_RegistersBinding_WithGivenIdentity_ReturnsId()
    {
        var store = new FakeOnSiteSignerBindingStore(resolved: null);
        var handler = new RegisterVerifiedSignerHandler(new FakeDocumentQueries(Document()), store);

        var id = await handler.Handle(Command(), CancellationToken.None);

        id.Should().NotBeEmpty();
        var registered = store.Registered.Should().ContainSingle().Subject;
        registered.SignerIdentity.Should().Be("Mandant Réel (SVV)");
        registered.CompanyId.Should().Be(CompanyId);
        registered.DocumentId.Should().Be(DocumentId);
        registered.RegisteredByUserId.Should().Be(OperatorId);
    }
}
