namespace Liakont.Host.Tests.Unit.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Contracts.Queries;

/// <summary>Double d'<see cref="IEmailTemplateQueries"/> renvoyant des jeux fixés (tests bUnit Notification, RB6 P2).</summary>
internal sealed class FakeEmailTemplateQueries : IEmailTemplateQueries
{
    private readonly IReadOnlyList<EmailTemplateDto> _templates;

    public FakeEmailTemplateQueries(IReadOnlyList<EmailTemplateDto>? templates = null) => _templates = templates ?? [];

    public Task<EmailTemplateDto?> GetByCode(string code, string languageCode, Guid? companyId, CancellationToken ct = default) =>
        Task.FromResult(_templates.FirstOrDefault(t => t.Code == code && t.LanguageCode == languageCode));

    public Task<EmailTemplateDto?> GetById(Guid templateId, CancellationToken ct = default) =>
        Task.FromResult(_templates.FirstOrDefault(t => t.Id == templateId) ?? (_templates.Count == 0 ? null : _templates[0]));

    public Task<IReadOnlyList<EmailTemplateDto>> List(Guid? companyId = null, CancellationToken ct = default) =>
        Task.FromResult(_templates);
}
