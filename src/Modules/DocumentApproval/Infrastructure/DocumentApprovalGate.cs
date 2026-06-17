namespace Liakont.Modules.DocumentApproval.Infrastructure;

using Dapper;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.DocumentApproval.Contracts.DTOs;
using Liakont.Modules.DocumentApproval.Domain;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Câblage de bout en bout de la Règle de gate (ADR-0028 §5, INV-APPROVAL-4 ; SIG06) : charge la tentative la
/// PLUS RÉCENTE (<c>attempt</c> max, ADR-0028 §6), résout le niveau de preuve requis du tenant (paramétrage V005,
/// défaut <c>Recorded</c>) sur la MÊME connexion, puis applique la fonction PURE de domaine
/// <see cref="ApprovalGate.Evaluate(Domain.Entities.DocumentValidation, Signature.Contracts.SignatureLevel)"/>.
/// Aucune mutation. Toujours scopé par <c>company_id</c> (CLAUDE.md n°9). Fail-closed (« bloquer plutôt qu'émettre
/// faux », CLAUDE.md n°3) quand aucune tentative n'existe.
/// </summary>
internal sealed class DocumentApprovalGate : IDocumentApprovalGate
{
    private const string MaxAttemptSql = """
        SELECT max(attempt)
        FROM documentapproval.document_validations
        WHERE company_id = @CompanyId AND document_id = @DocumentId AND validation_purpose = @Purpose
        """;

    private readonly IConnectionFactory _connectionFactory;

    public DocumentApprovalGate(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ApprovalGateResult> EvaluateAsync(
        Guid companyId, Guid documentId, ValidationPurpose purpose, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenAsync(ct);

        var maxAttempt = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                MaxAttemptSql,
                new { CompanyId = companyId, DocumentId = documentId, Purpose = (int)purpose },
                cancellationToken: ct));

        if (maxAttempt is null)
        {
            return Closed(
                "aucune validation enregistrée pour ce document et ce purpose : le gate reste fermé tant qu'une " +
                "acceptation/validation n'est pas acquise.");
        }

        var validation = await DocumentValidationMaterializer.LoadAsync(
            connection, companyId, documentId, purpose, maxAttempt.Value, transaction: null, ct);

        if (validation is null)
        {
            // Course rarissime : la tentative max a disparu entre les deux lectures. Fail-closed.
            return Closed("la tentative de validation est introuvable.");
        }

        var requiredLevel = await DocumentApprovalRequirementReader.ReadRequiredLevelAsync(
            connection, companyId, purpose, transaction: null, ct);

        var decision = ApprovalGate.Evaluate(validation, requiredLevel);
        return new ApprovalGateResult { IsOpen = decision.IsOpen, Reason = decision.Reason };
    }

    private static ApprovalGateResult Closed(string reason)
        => new() { IsOpen = false, Reason = $"Émission bloquée — {reason}" };
}
