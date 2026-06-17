namespace Liakont.Modules.DocumentApproval.Infrastructure;

using Dapper;
using Liakont.Modules.DocumentApproval.Contracts;
using Liakont.Modules.Signature.Contracts;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Paramétrage TENANT du niveau de preuve requis par purpose (V005 ; ADR-0028 §5 cond. 2, F17 §7 ; SIG06).
/// Configuration MUTABLE (pas une table d'audit) : upsert autorisé. Le niveau est exposé par son NOM à la
/// frontière <c>Contracts</c> (jamais l'enum <c>SignatureLevel</c>, qui reste interne — frontière DocumentApproval
/// → Signature.Contracts non franchie côté Contracts). Toujours scopé par <c>company_id</c> (CLAUDE.md n°9).
/// </summary>
internal sealed class PostgresDocumentApprovalRequirements : IDocumentApprovalRequirements
{
    private const string UpsertSql = """
        INSERT INTO documentapproval.document_approval_requirement
            (company_id, validation_purpose, required_level, updated_at)
        VALUES (@CompanyId, @Purpose, @RequiredLevel, now())
        ON CONFLICT (company_id, validation_purpose)
        DO UPDATE SET required_level = excluded.required_level, updated_at = now()
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresDocumentApprovalRequirements(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<string> GetRequiredLevelAsync(
        Guid companyId, ValidationPurpose purpose, CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenAsync(ct);
        var level = await DocumentApprovalRequirementReader.ReadRequiredLevelAsync(
            connection, companyId, purpose, transaction: null, ct);
        return level.ToString();
    }

    public async Task SetRequiredLevelAsync(
        Guid companyId, ValidationPurpose purpose, string requiredLevelName, CancellationToken ct = default)
    {
        var level = ParseApplicableLevel(requiredLevelName);

        using var connection = await _connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(
            new CommandDefinition(
                UpsertSql,
                new { CompanyId = companyId, Purpose = (int)purpose, RequiredLevel = (int)level },
                cancellationToken: ct));
    }

    /// <summary>
    /// Parse un niveau requis APPLICABLE : un niveau UNIQUE et non vide (<c>Recorded</c>/<c>SES</c>/<c>AES</c>/
    /// <c>QES</c>). Rejette <c>None</c> (le défaut « pas d'exigence » est <c>Recorded</c>, jamais <c>None</c>) et
    /// tout drapeau combiné — jamais une exigence silencieusement invalide (doublé par le CHECK SQL de V005).
    /// </summary>
    private static SignatureLevel ParseApplicableLevel(string requiredLevelName)
    {
        // Round-trip strict sur le NOM (le contrat n'accepte que des noms) : `Enum.TryParse("2")` réussirait et
        // rendrait SES — on rejette donc toute entrée dont le `ToString()` ne réécrit pas exactement l'entrée
        // (valeurs numériques, casse divergente). Le test du niveau unique exclut None et les ensembles combinés.
        if (!string.IsNullOrWhiteSpace(requiredLevelName)
            && Enum.TryParse<SignatureLevel>(requiredLevelName, ignoreCase: false, out var level)
            && string.Equals(level.ToString(), requiredLevelName, StringComparison.Ordinal)
            && level is SignatureLevel.Recorded or SignatureLevel.SES or SignatureLevel.AES or SignatureLevel.QES)
        {
            return level;
        }

        throw new ArgumentException(
            $"Niveau de preuve requis invalide : « {requiredLevelName} » (attendu : le NOM Recorded, SES, AES ou " +
            "QES — ni une valeur numérique, ni None, ni un ensemble combiné).",
            nameof(requiredLevelName));
    }
}
