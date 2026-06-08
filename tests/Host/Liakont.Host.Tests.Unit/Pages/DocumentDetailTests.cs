namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Liakont.Host.Components.Pages;
using Liakont.Host.Documents;
using Liakont.Modules.Documents.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class DocumentDetailTests : BunitContext
{
    private static readonly Guid DocId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public DocumentDetailTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
    }

    [Fact]
    public void Should_Render_Detail_And_Back_Link_When_Document_Found()
    {
        Services.AddScoped<IDocumentDetailConsoleQueries>(_ => FakeDetailQueries.Returning(BuildModel("2026-001")));

        var cut = Render<DocumentDetail>(p => p.Add(c => c.Id, DocId));

        cut.Find("[data-testid='document-detail-title']").TextContent.Should().Contain("2026-001");
        cut.FindAll("[data-testid='document-detail']").Should().ContainSingle("la vue détail est rendue");
        cut.FindAll("[data-testid='document-detail-back']").Should().ContainSingle("le retour à la liste est proposé");
        cut.FindAll("[data-testid='document-detail-notfound']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-detail-error']").Should().BeEmpty();
    }

    [Fact]
    public void Should_Show_NotFound_When_Document_Absent()
    {
        Services.AddScoped<IDocumentDetailConsoleQueries>(_ => FakeDetailQueries.Returning(null));

        var cut = Render<DocumentDetail>(p => p.Add(c => c.Id, DocId));

        cut.FindAll("[data-testid='document-detail-notfound']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail']").Should().BeEmpty("aucune fiche n'est rendue pour un document introuvable");
    }

    [Fact]
    public void Should_Show_Error_Banner_When_Load_Throws()
    {
        Services.AddScoped<IDocumentDetailConsoleQueries>(_ => FakeDetailQueries.Throwing());

        var cut = Render<DocumentDetail>(p => p.Add(c => c.Id, DocId));

        // L'échec reste VISIBLE (bandeau) et n'expose pas la fiche (anti faux-vert).
        cut.FindAll("[data-testid='document-detail-error']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-detail']").Should().BeEmpty();
    }

    private static DocumentDetailViewModel BuildModel(string number) => new()
    {
        Document = new DocumentDto
        {
            Id = DocId,
            SourceReference = $"src/{number}",
            DocumentNumber = number,
            DocumentType = "invoice",
            IssueDate = new DateOnly(2026, 6, 1),
            CustomerName = "DUPONT J.",
            CustomerIsCompanyHint = false,
            TotalNet = 1000m,
            TotalTax = 162.80m,
            TotalGross = 1162.80m,
            State = "Issued",
            PayloadHash = "sha256:payload",
            FirstSeenUtc = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            LastUpdateUtc = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
        },
        Events = [],
        BlockingReason = null,
        Archive = null,
        IsArchived = false,
    };

    private sealed class FakeDetailQueries : IDocumentDetailConsoleQueries
    {
        private readonly DocumentDetailViewModel? _model;
        private readonly bool _throws;

        private FakeDetailQueries(DocumentDetailViewModel? model, bool throws)
        {
            _model = model;
            _throws = throws;
        }

        public static FakeDetailQueries Returning(DocumentDetailViewModel? model) => new(model, throws: false);

        public static FakeDetailQueries Throwing() => new(null, throws: true);

        public Task<DocumentDetailViewModel?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (_throws)
            {
                throw new InvalidOperationException("Échec simulé de chargement du détail.");
            }

            return Task.FromResult(_model);
        }
    }
}
