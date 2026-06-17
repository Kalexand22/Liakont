namespace Liakont.Modules.DocumentApproval.Infrastructure;

using System.Data;
using Dapper;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.Signature.Contracts;

/// <summary>
/// Lecture du niveau de preuve requis (paramétrage tenant, V005) d'un purpose, partagée par le gate
/// (<see cref="DocumentApprovalGate"/>, même connexion que la matérialisation) et la surface de configuration
/// publique (<see cref="PostgresDocumentApprovalRequirements"/>). Toujours scopée par <c>company_id</c>
/// (CLAUDE.md n°9). En l'ABSENCE de ligne, applique le défaut <see cref="SignatureLevel.Recorded"/> (ADR-0028 §5 :
/// aucune exigence configurée ⇒ une acceptation enregistrée suffit ; jamais un blocage).
/// </summary>
internal static class DocumentApprovalRequirementReader
{
    private const string SelectSql = """
        SELECT required_level
        FROM documentapproval.document_approval_requirement
        WHERE company_id = @CompanyId AND validation_purpose = @Purpose
        """;

    public static async Task<SignatureLevel> ReadRequiredLevelAsync(
        IDbConnection connection,
        Guid companyId,
        ValidationPurpose purpose,
        IDbTransaction? transaction,
        CancellationToken ct)
    {
        var level = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition(
                SelectSql,
                new { CompanyId = companyId, Purpose = (int)purpose },
                transaction,
                cancellationToken: ct));

        return level is null ? SignatureLevel.Recorded : (SignatureLevel)level.Value;
    }
}
