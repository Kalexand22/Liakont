namespace Liakont.Modules.Signature.Infrastructure.OnSite;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.Documents.Contracts.Queries;
using Liakont.Modules.Signature.Application.OnSite;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Enregistre un SIGNATAIRE VÉRIFIÉ pour un document (ADR-0030 §5 ; INV-ONSITE-7) : le « mécanisme de liaison
/// VÉRIFIÉ séparé » de la capture. Re-vérifie d'abord <c>document_id → company_id</c> (tenant-scoping serveur,
/// CLAUDE.md n°9) → <c>NotFound</c> sinon, puis consigne la liaison en append-only. C'est la seule source d'un
/// <c>SignerIdentity</c> probant ; la capture la résout côté serveur, jamais depuis son payload.
/// </summary>
internal sealed class RegisterVerifiedSignerHandler : IRequestHandler<RegisterVerifiedSignerCommand, Guid>
{
    private readonly IDocumentQueries _documents;
    private readonly IOnSiteSignerBindingStore _signerBindings;

    public RegisterVerifiedSignerHandler(IDocumentQueries documents, IOnSiteSignerBindingStore signerBindings)
    {
        _documents = documents;
        _signerBindings = signerBindings;
    }

    public async Task<Guid> Handle(RegisterVerifiedSignerCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Tenant-scoping serveur : un document d'un autre tenant est introuvable (database-per-tenant) → 404.
        _ = await _documents.GetByIdAsync(request.DocumentId, cancellationToken)
            ?? throw new NotFoundException("Document", request.DocumentId);

        var record = new OnSiteSignerBindingRecord
        {
            Id = Guid.NewGuid(),
            CompanyId = request.CompanyId,
            DocumentId = request.DocumentId,
            SignerIdentity = request.SignerIdentity,
            VerificationMethod = request.VerificationMethod,
            RegisteredByUserId = request.RegisteredByUserId,
            VerifiedAtUtc = DateTimeOffset.UtcNow,
        };
        await _signerBindings.RegisterAsync(record, cancellationToken);
        return record.Id;
    }
}
