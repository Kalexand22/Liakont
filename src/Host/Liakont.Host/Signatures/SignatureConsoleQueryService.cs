namespace Liakont.Host.Signatures;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Contracts.Queries;
using Liakont.Modules.Signature.Contracts;
using Stratum.Common.Abstractions.Security;

/// <summary>
/// Implémentation de <see cref="ISignatureConsoleQueries"/> : compose les lectures du module DocumentApproval
/// (<see cref="IDocumentApprovalQueries"/>, SIG04) et le registre de fournisseurs de signature
/// (<see cref="ISignatureProviderRegistry"/>, SIG03). AUCUNE logique métier ni accès base direct — pure
/// composition de ports <c>Contracts</c>. Le <c>company_id</c> est résolu depuis
/// <see cref="IActorContextAccessor"/> (jamais fourni par le client) ; une absence de tenant résolu lève
/// (on bloque plutôt que de lire faux / cross-tenant, CLAUDE.md n°3/9/17).
/// </summary>
internal sealed class SignatureConsoleQueryService : ISignatureConsoleQueries
{
    private readonly IDocumentApprovalQueries _approvals;
    private readonly ISignatureProviderRegistry _providers;
    private readonly IActorContextAccessor _actorAccessor;

    public SignatureConsoleQueryService(
        IDocumentApprovalQueries approvals,
        ISignatureProviderRegistry providers,
        IActorContextAccessor actorAccessor)
    {
        _approvals = approvals;
        _providers = providers;
        _actorAccessor = actorAccessor;
    }

    public async Task<SignatureStatusView> GetStatusAsync(
        Guid documentId, ValidationPurpose purpose, CancellationToken cancellationToken = default)
    {
        var companyId = ResolveCompanyId();
        var latest = await _approvals.GetLatestAttempt(companyId, documentId, purpose, cancellationToken).ConfigureAwait(false);
        var log = await _approvals.GetApprovalLog(companyId, documentId, purpose, cancellationToken).ConfigureAwait(false);
        return new SignatureStatusView { Latest = latest, Log = log };
    }

    public IReadOnlyCollection<string> GetConfiguredProviderTypes() => _providers.RegisteredTypes;

    /// <summary>Résout le tenant (company_id) depuis le contexte d'acteur ; lève si non résolu (CLAUDE.md n°9).</summary>
    private Guid ResolveCompanyId()
    {
        var actor = _actorAccessor.Current;
        if (actor.CompanyId is not { } companyId)
        {
            throw new InvalidOperationException(
                "Tenant non résolu : la consultation des validations exige un contexte de tenant (company_id).");
        }

        return companyId;
    }
}
