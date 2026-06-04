namespace Liakont.Modules.Documents.Infrastructure.Deduplication;

using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Liakont.Modules.Documents.Contracts.Deduplication;
using Liakont.Modules.Documents.Domain.Deduplication;
using Liakont.Modules.Documents.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Anti-doublon F06 §4 (item TRK03) sur la base DU TENANT courant (<see cref="IConnectionFactory"/> route
/// vers le tenant résolu — database-per-tenant, blueprint §7 ; aucune comparaison cross-tenant possible,
/// CLAUDE.md n°9/17). Cette implémentation ne fait que LIRE les antécédents en base puis délègue le verdict
/// à la fonction pure <see cref="DocumentDuplicatePolicy"/> (les quatre règles de F06 §4) : la décision
/// fiscale/opérationnelle reste testable hors base et tracée à la spec (aucune règle inventée, CLAUDE.md n°2).
/// </summary>
public sealed class PostgresDuplicateDocumentCheck : IDuplicateDocumentCheck
{
    private const string PriorsBySupplierAndNumberSql = """
        SELECT id, state
        FROM documents.documents
        WHERE supplier_siren = @SupplierSiren
          AND document_number = @DocumentNumber
          AND id <> @CandidateId
        ORDER BY last_update_utc DESC
        """;

    private const string IssuedWithSameHashSql = """
        SELECT id
        FROM documents.documents
        WHERE payload_hash = @PayloadHash
          AND state = @IssuedState
          AND id <> @CandidateId
        ORDER BY last_update_utc DESC
        LIMIT 1
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresDuplicateDocumentCheck(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<DuplicateCheckResult> EvaluateAsync(DuplicateCheckRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // F06 §4.1-4.3 : antécédents de même clé fonctionnelle (supplier_siren, document_number). Sans SIREN,
        // la clé fonctionnelle est incomplète : on ne peut affirmer « même fournisseur », on n'en déduit donc
        // aucun antécédent (le garde-fou d'empreinte 4.4 reste actif). On n'invente pas un rapprochement.
        IReadOnlyCollection<PriorDocumentMatch> priors;
        if (string.IsNullOrWhiteSpace(request.SupplierSiren))
        {
            priors = Array.Empty<PriorDocumentMatch>();
        }
        else
        {
            var rows = await conn.QueryAsync(new CommandDefinition(
                PriorsBySupplierAndNumberSql,
                new
                {
                    request.SupplierSiren,
                    request.DocumentNumber,
                    CandidateId = request.DocumentId,
                },
                cancellationToken: cancellationToken));

            priors = rows
                .Select(row => new PriorDocumentMatch(
                    (Guid)row.id,
                    Enum.Parse<DocumentState>((string)row.state)))
                .ToList();
        }

        // F06 §4.4 : empreinte de payload identique à un document déjà émis (toute clé fonctionnelle).
        var issuedHashId = await conn.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            IssuedWithSameHashSql,
            new
            {
                request.PayloadHash,
                IssuedState = nameof(DocumentState.Issued),
                CandidateId = request.DocumentId,
            },
            cancellationToken: cancellationToken));

        var decision = DocumentDuplicatePolicy.Decide(priors, issuedHashId);

        return new DuplicateCheckResult
        {
            Decision = ToContract(decision.Outcome),
            RelatedDocumentId = decision.RelatedDocumentId,
        };
    }

    private static DuplicateCheckDecision ToContract(DocumentDuplicateOutcome outcome) => outcome switch
    {
        DocumentDuplicateOutcome.Send => DuplicateCheckDecision.Send,
        DocumentDuplicateOutcome.ResendSupersedingRejected => DuplicateCheckDecision.ResendSupersedingRejected,
        DocumentDuplicateOutcome.BlockedAlreadyIssued => DuplicateCheckDecision.BlockedAlreadyIssued,
        DocumentDuplicateOutcome.BlockedStrictDuplicate => DuplicateCheckDecision.BlockedStrictDuplicate,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Verdict d'anti-doublon non pris en charge."),
    };
}
