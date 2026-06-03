namespace Stratum.Modules.Notification.Contracts.Queries;

using Stratum.Modules.Notification.Contracts.DTOs;

public interface IEmailTemplateQueries
{
    Task<EmailTemplateDto?> GetByCode(string code, string languageCode, Guid? companyId, CancellationToken ct = default);

    Task<EmailTemplateDto?> GetById(Guid templateId, CancellationToken ct = default);

    Task<IReadOnlyList<EmailTemplateDto>> List(Guid? companyId = null, CancellationToken ct = default);
}
