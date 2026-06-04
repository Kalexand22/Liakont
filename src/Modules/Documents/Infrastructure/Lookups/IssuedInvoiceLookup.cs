namespace Liakont.Modules.Documents.Infrastructure.Lookups;

using Dapper;
using Liakont.Agent.Contracts.Pivot;
using Liakont.Modules.Documents.Domain.Entities;
using Liakont.Modules.Validation.Contracts.CreditNotes;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Implémentation réelle (item TRK03) du port <see cref="IIssuedInvoiceLookup"/> déclaré par le module
/// Validation (VAL04, avoirs F07-F08 §B.5) : distingue un avoir régulier d'un avoir ORPHELIN selon l'état
/// de la facture d'origine référencée. Lecture sur la base DU TENANT courant (<see cref="IConnectionFactory"/>
/// — la connexion EST le tenant, database-per-tenant blueprint §7 ; isolation par la connexion, aucune
/// requête cross-tenant — CLAUDE.md n°9/17). Le rapprochement se fait par NUMÉRO (EN 16931 BT-25, clé
/// fonctionnelle de la référence) ; un lookup non concluant retourne <see cref="OriginalInvoiceStatus.Unknown"/>
/// (fail-safe : « bloquer plutôt qu'envoyer faux », CLAUDE.md n°3 — jamais de référence fabriquée, n°2).
/// </summary>
public sealed class IssuedInvoiceLookup : IIssuedInvoiceLookup
{
    private const string OriginalStatusByNumberSql = """
        SELECT
            EXISTS(SELECT 1 FROM documents.documents
                   WHERE document_number = @Number AND state = @IssuedState) AS issued,
            EXISTS(SELECT 1 FROM documents.documents
                   WHERE document_number = @Number) AS known
        """;

    private readonly IConnectionFactory _connectionFactory;

    public IssuedInvoiceLookup(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<OriginalInvoiceStatus> FindOriginalStatusAsync(
        Guid companyId,
        PivotDocumentRefDto originalReference,
        CancellationToken cancellationToken = default)
    {
        // Référence absente/incomplète : avoir orphelin (Unknown) — fail-safe, jamais de référence fabriquée.
        if (originalReference is null || string.IsNullOrWhiteSpace(originalReference.Number))
        {
            return OriginalInvoiceStatus.Unknown;
        }

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var row = await conn.QueryFirstAsync(new CommandDefinition(
            OriginalStatusByNumberSql,
            new { originalReference.Number, IssuedState = nameof(DocumentState.Issued) },
            cancellationToken: cancellationToken));

        if ((bool)row.issued)
        {
            return OriginalInvoiceStatus.KnownIssued;
        }

        return (bool)row.known
            ? OriginalInvoiceStatus.KnownNotIssued
            : OriginalInvoiceStatus.Unknown;
    }
}
