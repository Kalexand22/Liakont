namespace Liakont.Modules.Mandats.Infrastructure;

using Dapper;
using Liakont.Modules.Mandats.Contracts;
using Liakont.Modules.Mandats.Domain.Entities;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Allocateur Dapper du BT-1 fiscal 389 (ADR-0025, F15 §3 — MND05). GET-OR-CREATE idempotent sur la clé source
/// (<c>source_reference</c>) avec verrou de séquence PAR MANDANT (<c>FOR UPDATE</c>), tout dans une transaction
/// unique du schéma <c>mandats</c> (base du tenant). L'allocation, l'avancement de la séquence et l'assignation
/// du numéro à l'acceptation sont atomiques entre eux ; l'atomicité avec l'écriture du document (autre module)
/// n'est pas recherchée — elle est REMPLACÉE par l'idempotence rejouable sur la clé source (ADR-0025 §2).
/// </summary>
internal sealed class PostgresSelfBilledNumberAllocator : ISelfBilledNumberAllocator
{
    private const string ReadMandantPrefixSql = """
        SELECT numbering_prefix
        FROM mandats.mandants
        WHERE company_id = @CompanyId AND id = @MandantId
        """;

    // Crée la séquence du mandant si absente (préfixe figé seedé depuis le mandant). ON CONFLICT DO NOTHING
    // ferme la course de création concurrente : la ligne existe ensuite à coup sûr pour le FOR UPDATE.
    private const string EnsureSequenceSql = """
        INSERT INTO mandats.mandat_sequences (company_id, mandant_id, prefix, next_value, created_at)
        VALUES (@CompanyId, @MandantId, @Prefix, @NextValue, @CreatedAt)
        ON CONFLICT (company_id, mandant_id) DO NOTHING
        """;

    // VERROU PAR MANDANT (INV-BT1-4) : sérialise les allocations concurrentes d'un même mandant (chronologie +
    // continuité §1.4). Des mandants différents = lignes différentes = aucune contention.
    private const string LockSequenceForUpdateSql = """
        SELECT company_id, mandant_id, prefix, next_value, created_at, updated_at
        FROM mandats.mandat_sequences
        WHERE company_id = @CompanyId AND mandant_id = @MandantId
        FOR UPDATE
        """;

    private const string ReadAllocationSql = """
        SELECT allocated_number
        FROM mandats.mandat_number_allocations
        WHERE company_id = @CompanyId AND mandant_id = @MandantId AND source_reference = @SourceReference
        """;

    private const string AdvanceSequenceSql = """
        UPDATE mandats.mandat_sequences
        SET next_value = @NextValue, updated_at = @UpdatedAt
        WHERE company_id = @CompanyId AND mandant_id = @MandantId
        """;

    private const string InsertAllocationSql = """
        INSERT INTO mandats.mandat_number_allocations
            (company_id, mandant_id, source_reference, allocated_value, allocated_number)
        VALUES
            (@CompanyId, @MandantId, @SourceReference, @AllocatedValue, @AllocatedNumber)
        """;

    // Assigne le BT-1 fiscal à l'acceptation du document (HORS payload hashé — INV-BT1-1). N'est PAS une
    // transition d'état : on ne touche ni `state` ni `updated_at` (qui datent la dernière TRANSITION, ADR-0024) ;
    // la trace permanente de l'allocation est la ligne `mandat_number_allocations`, pas une entrée de journal.
    private const string AssignAcceptanceSql = """
        UPDATE mandats.self_billed_acceptances
        SET allocated_number = @AllocatedNumber
        WHERE company_id = @CompanyId AND document_id = @DocumentId
        """;

    private readonly IConnectionFactory _connectionFactory;

    public PostgresSelfBilledNumberAllocator(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<string> AllocateAsync(
        Guid companyId,
        Guid mandantId,
        Guid documentId,
        string sourceReference,
        CancellationToken ct = default)
    {
        // INV-BT1-3 (substitution, pas affaiblissement) : un 389 sans clé d'idempotence source n'est pas
        // numérotable → rejet, jamais « laisser passer » (CLAUDE.md n°3).
        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            throw new ArgumentException(
                "La clé d'idempotence source (source_reference) est obligatoire pour numéroter un 389 (substitution d'invariant, ADR-0025 §4).",
                nameof(sourceReference));
        }

        await using var txn = await TransactionScope.BeginAsync(_connectionFactory, ct);

        // 1. Résoudre le préfixe propre au mandant (paramétrage tenant). Mandant inconnu ⇒ fail-closed : on ne
        //    devine pas une racine de numérotation (CLAUDE.md n°2/3).
        var prefix = await txn.Connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(
                ReadMandantPrefixSql,
                new { CompanyId = companyId, MandantId = mandantId },
                txn.Transaction,
                cancellationToken: ct));

        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new InvalidOperationException(
                $"Mandant {mandantId} introuvable (ou sans préfixe) pour ce tenant : le BT-1 fiscal 389 ne peut être alloué — document maintenu bloqué (CLAUDE.md n°3, ADR-0025 §5).");
        }

        // 2. Garantir la séquence du mandant (préfixe figé) puis la VERROUILLER (FOR UPDATE, par mandant).
        var seed = MandatSequence.Start(companyId, mandantId, prefix);
        await txn.Connection.ExecuteAsync(
            new CommandDefinition(
                EnsureSequenceSql,
                new { seed.CompanyId, seed.MandantId, seed.Prefix, seed.NextValue, seed.CreatedAt },
                txn.Transaction,
                cancellationToken: ct));

        var sequence = await LockSequenceForUpdateAsync(txn, companyId, mandantId, ct);

        // 3. Idempotence SOUS VERROU : un numéro déjà alloué pour cette clé source est RELU, jamais ré-alloué
        //    (INV-BT1-2). La séquence n'avance pas. Une allocation concurrente de la même source a été sérialisée
        //    par le FOR UPDATE ci-dessus : à la reprise, elle relit le numéro que l'autre transaction a posé.
        var existing = await txn.Connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(
                ReadAllocationSql,
                new { CompanyId = companyId, MandantId = mandantId, SourceReference = sourceReference },
                txn.Transaction,
                cancellationToken: ct));

        string formatted;
        if (existing is not null)
        {
            formatted = existing;
        }
        else
        {
            var allocation = sequence.Allocate();
            await txn.Connection.ExecuteAsync(
                new CommandDefinition(
                    AdvanceSequenceSql,
                    new { CompanyId = companyId, MandantId = mandantId, sequence.NextValue, sequence.UpdatedAt },
                    txn.Transaction,
                    cancellationToken: ct));
            await txn.Connection.ExecuteAsync(
                new CommandDefinition(
                    InsertAllocationSql,
                    new
                    {
                        CompanyId = companyId,
                        MandantId = mandantId,
                        SourceReference = sourceReference,
                        AllocatedValue = allocation.Value,
                        AllocatedNumber = allocation.FormattedNumber,
                    },
                    txn.Transaction,
                    cancellationToken: ct));
            formatted = allocation.FormattedNumber;
        }

        // 4. Assigner le BT-1 fiscal à l'acceptation du document (HORS payload hashé — INV-BT1-1, « assignée à
        //    l'émission »). Aucune acceptation ⇒ fail-closed : un document self-billed atteint l'allocation APRÈS
        //    son acceptation (le gate a déjà lu son enregistrement). Jamais un no-op silencieux.
        var affected = await txn.Connection.ExecuteAsync(
            new CommandDefinition(
                AssignAcceptanceSql,
                new { CompanyId = companyId, DocumentId = documentId, AllocatedNumber = formatted },
                txn.Transaction,
                cancellationToken: ct));

        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Aucune acceptation self-billed pour le document {documentId} de ce tenant : le BT-1 fiscal ne peut être assigné (l'allocation suit l'acceptation — ADR-0025 §5).");
        }

        await txn.CommitAsync(ct);
        return formatted;
    }

    private static async Task<MandatSequence> LockSequenceForUpdateAsync(
        TransactionScope txn, Guid companyId, Guid mandantId, CancellationToken ct)
    {
        var row = await txn.Connection.QuerySingleAsync(
            new CommandDefinition(
                LockSequenceForUpdateSql,
                new { CompanyId = companyId, MandantId = mandantId },
                txn.Transaction,
                cancellationToken: ct));

        return MandatSequence.Reconstitute(
            (Guid)row.company_id,
            (Guid)row.mandant_id,
            (string)row.prefix,
            (long)row.next_value,
            MandatsRowReader.ToDateTimeOffset((object)row.created_at),
            MandatsRowReader.ToNullableDateTimeOffset((object?)row.updated_at));
    }
}
