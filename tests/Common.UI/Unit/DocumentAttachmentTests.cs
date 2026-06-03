namespace Stratum.Common.UI.Tests.Unit;

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Stratum.Common.Abstractions.Documents;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.UI.Components;
using Stratum.Common.UI.Models;
using Stratum.Common.UI.Services;
using Xunit;

public sealed class DocumentAttachmentTests : BunitContext
{
    private readonly FakeDocumentAttachmentService _service = new();
    private readonly FakePermissionService _permissions = new();

    public DocumentAttachmentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IDocumentAttachmentService>(_service);
        Services.AddSingleton<IPermissionService>(_permissions);
        Services.AddSingleton<IToastService>(new NullToastService());
    }

    [Fact]
    public void ShouldRenderEmptyMessageWhenNoDocuments()
    {
        var cut = Render<DocumentAttachment>(p => p
            .Add(c => c.EntityType, "Party")
            .Add(c => c.EntityId, "123"));

        cut.Find("[data-testid='doc-attach-empty']").TextContent
            .Should().Contain("Aucun document");
    }

    [Fact]
    public void ShouldRenderDocumentList()
    {
        _service.Add(new AttachedDocumentInfo(
            Guid.NewGuid(), "test.pdf", "application/pdf", 1024, DateTimeOffset.UtcNow));

        var cut = Render<DocumentAttachment>(p => p
            .Add(c => c.EntityType, "Party")
            .Add(c => c.EntityId, "123"));

        cut.Find("[data-testid='doc-attach-list']").Should().NotBeNull();
        cut.Find("[data-testid='doc-attach-name']").TextContent.Should().Be("test.pdf");
    }

    [Fact]
    public void ShouldRenderMultipleDocuments()
    {
        _service.Add(new AttachedDocumentInfo(
            Guid.NewGuid(), "a.pdf", "application/pdf", 1024, DateTimeOffset.UtcNow));
        _service.Add(new AttachedDocumentInfo(
            Guid.NewGuid(), "b.png", "image/png", 2048, DateTimeOffset.UtcNow));

        var cut = Render<DocumentAttachment>(p => p
            .Add(c => c.EntityType, "Party")
            .Add(c => c.EntityId, "123"));

        cut.FindAll("[data-testid^='doc-attach-item-']").Count.Should().Be(2);
    }

    [Fact]
    public void ShouldRenderUploadZone()
    {
        var cut = Render<DocumentAttachment>(p => p
            .Add(c => c.EntityType, "Party")
            .Add(c => c.EntityId, "123"));

        cut.Find("[data-testid='doc-attach-dropzone']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldHideUploadWhenNoPermission()
    {
        _permissions.DenyAll = true;

        var cut = Render<DocumentAttachment>(p => p
            .Add(c => c.EntityType, "Party")
            .Add(c => c.EntityId, "123"));

        cut.FindAll("[data-testid='doc-attach-dropzone']").Count.Should().Be(0);
    }

    [Fact]
    public void ShouldRenderDownloadLink()
    {
        var docId = Guid.NewGuid();
        _service.Add(new AttachedDocumentInfo(
            docId, "test.pdf", "application/pdf", 1024, DateTimeOffset.UtcNow));

        var cut = Render<DocumentAttachment>(p => p
            .Add(c => c.EntityType, "Party")
            .Add(c => c.EntityId, "123"));

        var link = cut.Find($"[data-testid='doc-attach-download-{docId}']");
        link.TagName.Should().Be("A");
        link.GetAttribute("href").Should().Contain($"/documents/{docId}/download");
    }

    [Fact]
    public void ShouldRenderCustomTitle()
    {
        var cut = Render<DocumentAttachment>(p => p
            .Add(c => c.EntityType, "Party")
            .Add(c => c.EntityId, "123")
            .Add(c => c.Title, "Pièces jointes"));

        cut.Markup.Should().Contain("Pièces jointes");
    }

    [Fact]
    public void ShouldShowDeleteConfirmDialog()
    {
        var docId = Guid.NewGuid();
        _service.Add(new AttachedDocumentInfo(
            docId, "test.pdf", "application/pdf", 1024, DateTimeOffset.UtcNow));

        var cut = Render<DocumentAttachment>(p => p
            .Add(c => c.EntityType, "Party")
            .Add(c => c.EntityId, "123"));

        cut.Find($"[data-testid='doc-attach-delete-{docId}']").Click();

        cut.Find("[data-testid='doc-attach-delete-dialog']").Should().NotBeNull();
        cut.Markup.Should().Contain("test.pdf");
    }

    [Fact]
    public void ShouldConfirmDeleteAndRemoveDocument()
    {
        var docId = Guid.NewGuid();
        _service.Add(new AttachedDocumentInfo(
            docId, "test.pdf", "application/pdf", 1024, DateTimeOffset.UtcNow));

        var cut = Render<DocumentAttachment>(p => p
            .Add(c => c.EntityType, "Party")
            .Add(c => c.EntityId, "123"));

        cut.Find($"[data-testid='doc-attach-delete-{docId}']").Click();
        cut.Find("[data-testid='doc-attach-delete-confirm']").Click();

        cut.FindAll("[data-testid^='doc-attach-item-']").Count.Should().Be(0);
        cut.Find("[data-testid='doc-attach-empty']").Should().NotBeNull();
    }

    [Fact]
    public void ShouldCancelDeleteAndKeepDocument()
    {
        var docId = Guid.NewGuid();
        _service.Add(new AttachedDocumentInfo(
            docId, "test.pdf", "application/pdf", 1024, DateTimeOffset.UtcNow));

        var cut = Render<DocumentAttachment>(p => p
            .Add(c => c.EntityType, "Party")
            .Add(c => c.EntityId, "123"));

        cut.Find($"[data-testid='doc-attach-delete-{docId}']").Click();
        cut.Find("[data-testid='doc-attach-delete-cancel']").Click();

        cut.FindAll("[data-testid^='doc-attach-item-']").Count.Should().Be(1);
    }

    private sealed class FakeDocumentAttachmentService : IDocumentAttachmentService
    {
        private readonly List<AttachedDocumentInfo> _docs = [];

        public long MaxFileSizeBytes => 10 * 1024 * 1024;

        public void Add(AttachedDocumentInfo doc) => _docs.Add(doc);

        public Task<IReadOnlyList<AttachedDocumentInfo>> ListByEntityAsync(
            string entityType,
            string entityId,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AttachedDocumentInfo>>(_docs.ToList());

        public Task<Guid> UploadAsync(
            Stream content,
            string fileName,
            string contentType,
            long sizeBytes,
            string entityType,
            string entityId,
            CancellationToken ct)
        {
            var id = Guid.NewGuid();
            _docs.Add(new AttachedDocumentInfo(id, fileName, contentType, sizeBytes, DateTimeOffset.UtcNow));
            return Task.FromResult(id);
        }

        public Task DeleteAsync(Guid documentId, CancellationToken ct)
        {
            _docs.RemoveAll(d => d.Id == documentId);
            return Task.CompletedTask;
        }

        public string GetDownloadUrl(Guid documentId) => $"/documents/{documentId}/download";
    }

    private sealed class FakePermissionService : IPermissionService
    {
#pragma warning disable CS0067 // Event never used — required by interface
        public event Action? OnPermissionsChanged;
#pragma warning restore CS0067

        public bool DenyAll { get; set; }

        public bool HasPermission(string permission) => !DenyAll;
    }

    private sealed class NullToastService : IToastService
    {
#pragma warning disable CS0067 // Event never used — required by interface
        public event Action? OnToastsChanged;
#pragma warning restore CS0067

        public IReadOnlyList<ToastMessage> GetActiveToasts() => [];

        public void Show(string message, Severity severity, int duration = 5000, bool dismissible = true)
        {
        }

        public void Dismiss(Guid id)
        {
        }
    }
}
