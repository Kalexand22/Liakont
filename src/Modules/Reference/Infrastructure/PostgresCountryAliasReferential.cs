namespace Liakont.Modules.Reference.Infrastructure;

using Dapper;
using Liakont.Modules.Reference.Contracts;
using Liakont.Modules.Reference.Contracts.DTOs;
using Stratum.Common.Infrastructure.Database;

/// <summary>
/// Référentiel de correspondance pays (ADR-0038) : lecture (résolution read-time + liste console) ET écriture
/// (upsert/remove journalisés append-only), en base SYSTÈME via <see cref="ISystemConnectionFactory"/> (table
/// cross-instance universelle, aucun <c>tenant_id</c>). Singleton : la résolution (chemin chaud CHECK/SEND) est
/// servie depuis un cache mémoire INVALIDÉ à chaque écriture admin (petit volume). La cible ISO est VALIDÉE à
/// l'écriture (<see cref="IsoCountryReference"/>, INV-REF-CTRY-03) et la clé source normalisée (Trim + MAJ) ;
/// chaque mutation écrit une entrée de journal dans la MÊME transaction que la table (atomicité, CLAUDE.md n°4).
/// </summary>
internal sealed class PostgresCountryAliasReferential : ICountryAliasReferential, IDisposable
{
    private readonly ISystemConnectionFactory _systemConnectionFactory;
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private volatile IReadOnlyDictionary<string, string>? _cache;

    // Époque monotone incrémentée à CHAQUE invalidation. Un chargement démarré à une époque antérieure ne
    // republie pas sa carte (voir GetMapAsync) : garantit qu'une écriture committée pendant un chargement
    // concurrent n'est jamais durablement invisible (ADR §7 « cache invalidé à chaque écriture »).
    private long _cacheEpoch;

    public PostgresCountryAliasReferential(ISystemConnectionFactory systemConnectionFactory)
    {
        _systemConnectionFactory = systemConnectionFactory;
    }

    public async Task<string?> ResolveAsync(string? rawCountryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawCountryCode))
        {
            return rawCountryCode;
        }

        var map = await GetMapAsync(cancellationToken);
        return map.TryGetValue(rawCountryCode.Trim().ToUpperInvariant(), out var iso) ? iso : rawCountryCode;
    }

    public async Task<IReadOnlyList<CountryAliasDto>> GetAliasesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT source_code, iso_code, updated_at
            FROM reference.country_alias
            ORDER BY source_code ASC
            """;

        using var connection = await _systemConnectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        var result = new List<CountryAliasDto>();
        foreach (var row in rows)
        {
            result.Add(new CountryAliasDto
            {
                SourceCode = (string)row.source_code,
                IsoCode = (string)row.iso_code,
                UpdatedAtUtc = ToUtc((object)row.updated_at),
            });
        }

        return result;
    }

    /// <summary>
    /// Ajoute/met à jour une correspondance : normalise la clé source (Trim + MAJ), VALIDE la cible ISO (refusée
    /// si non-ISO, message opérateur FR), écrit la table + le journal append-only dans la MÊME transaction, puis
    /// invalide le cache. Renvoie la nature réellement appliquée (Create/Update).
    /// </summary>
    public async Task<CountryAliasChangeType> UpsertAsync(
        string sourceCode,
        string isoCode,
        Guid operatorId,
        string? operatorName,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = Normalize(sourceCode);
        if (normalizedSource.Length == 0)
        {
            throw new InvalidOperationException(
                "Le code source de la correspondance pays est vide : saisissez le code tel qu'il arrive de la source (ex. « BEL »).");
        }

        var normalizedIso = Normalize(isoCode);
        if (!IsoCountryReference.IsValid(normalizedIso))
        {
            throw new InvalidOperationException(
                $"Le code pays cible « {normalizedIso} » n'est pas un code ISO 3166-1 alpha-2 valide : saisissez un code officiel à 2 lettres (ex. « BE » pour la Belgique).");
        }

        const string readBeforeSql = "SELECT iso_code FROM reference.country_alias WHERE source_code = @SourceCode";
        const string upsertSql = """
            INSERT INTO reference.country_alias (source_code, iso_code, updated_at)
            VALUES (@SourceCode, @IsoCode, @UpdatedAt)
            ON CONFLICT (source_code) DO UPDATE
            SET iso_code = EXCLUDED.iso_code,
                updated_at = EXCLUDED.updated_at
            """;

        await using var txn = await TransactionScope.BeginAsync(
            new SystemConnectionFactoryAdapter(_systemConnectionFactory), cancellationToken);

        var before = await txn.Connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            readBeforeSql,
            new { SourceCode = normalizedSource },
            txn.Transaction,
            cancellationToken: cancellationToken));

        await txn.Connection.ExecuteAsync(new CommandDefinition(
            upsertSql,
            new { SourceCode = normalizedSource, IsoCode = normalizedIso, UpdatedAt = DateTimeOffset.UtcNow },
            txn.Transaction,
            cancellationToken: cancellationToken));

        var entry = CountryAliasChangeLogFactory.ForUpsert(normalizedSource, before, normalizedIso, operatorId, operatorName);
        await InsertChangeLogAsync(txn, entry, cancellationToken);

        await txn.CommitAsync(cancellationToken);

        Invalidate();
        return entry.ChangeType;
    }

    /// <summary>
    /// Supprime une correspondance (normalise la clé) : écrit la table + le journal dans la MÊME transaction,
    /// puis invalide le cache. Renvoie <c>false</c> si la correspondance n'existait pas (aucun effet, aucune
    /// entrée de journal orpheline — rien n'a muté).
    /// </summary>
    public async Task<bool> RemoveAsync(
        string sourceCode,
        Guid operatorId,
        string? operatorName,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = Normalize(sourceCode);

        const string readBeforeSql = "SELECT iso_code FROM reference.country_alias WHERE source_code = @SourceCode";
        const string deleteSql = "DELETE FROM reference.country_alias WHERE source_code = @SourceCode";

        await using var txn = await TransactionScope.BeginAsync(
            new SystemConnectionFactoryAdapter(_systemConnectionFactory), cancellationToken);

        var before = await txn.Connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            readBeforeSql,
            new { SourceCode = normalizedSource },
            txn.Transaction,
            cancellationToken: cancellationToken));

        if (before is null)
        {
            // Rien à supprimer : pas de mutation, donc pas d'entrée de journal (jamais d'entrée orpheline).
            return false;
        }

        await txn.Connection.ExecuteAsync(new CommandDefinition(
            deleteSql,
            new { SourceCode = normalizedSource },
            txn.Transaction,
            cancellationToken: cancellationToken));

        var entry = CountryAliasChangeLogFactory.ForRemove(normalizedSource, before, operatorId, operatorName);
        await InsertChangeLogAsync(txn, entry, cancellationToken);

        await txn.CommitAsync(cancellationToken);

        Invalidate();
        return true;
    }

    /// <summary>Libère le verrou de cache. Appelé par le conteneur DI à l'arrêt (singleton).</summary>
    public void Dispose() => _cacheGate.Dispose();

    private static Task<int> InsertChangeLogAsync(
        TransactionScope txn, CountryAliasChangeLogEntry entry, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO reference.country_alias_change_log
                (id, source_code, change_type, before_value, after_value, operator_id, operator_name, occurred_at)
            VALUES
                (@Id, @SourceCode, @ChangeType, @BeforeValue::jsonb, @AfterValue::jsonb, @OperatorId, @OperatorName, @OccurredAt)
            """;

        return txn.Connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                entry.Id,
                entry.SourceCode,
                ChangeType = (int)entry.ChangeType,
                BeforeValue = entry.BeforeJson,
                AfterValue = entry.AfterJson,
                entry.OperatorId,
                entry.OperatorName,
                entry.OccurredAt,
            },
            txn.Transaction,
            cancellationToken: cancellationToken));
    }

    private static string Normalize(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    private static DateTimeOffset ToUtc(object value) => value switch
    {
        DateTimeOffset dto => dto.ToUniversalTime(),
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero),
        _ => throw new InvalidOperationException("Horodatage de correspondance pays illisible en base."),
    };

    private void Invalidate()
    {
        // Incrément AVANT le vidage : un chargement en cours (démarré à l'ancienne époque) verra l'époque changer
        // et ne republiera pas son instantané périmé. Interlocked = barrière mémoire complète.
        Interlocked.Increment(ref _cacheEpoch);
        _cache = null;
    }

    private async Task<IReadOnlyDictionary<string, string>> GetMapAsync(CancellationToken cancellationToken)
    {
        var cached = _cache;
        if (cached is not null)
        {
            return cached;
        }

        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            if (_cache is not null)
            {
                return _cache;
            }

            // Époque capturée AVANT le SELECT. Invalidate() n'étant PAS sous ce verrou, une écriture peut committer
            // + invalider À TOUT MOMENT du chargement. On publie puis on RE-VÉRIFIE l'époque : si elle a bougé, on
            // retire la carte qu'on vient de publier (elle peut être périmée) — la résolution suivante rechargera
            // l'état committé. Le recheck APRÈS l'affectation ferme la fenêtre compare-puis-publie : Interlocked =
            // ordre séquentiel sur l'époque, donc si une invalidation a « perdu » son _cache=null (écrasé par notre
            // publication), son incrément d'époque est forcément visible ici → on re-nullifie. Une écriture committée
            // n'est jamais durablement invisible (ADR §7).
            var epochAtStart = Interlocked.Read(ref _cacheEpoch);
            var loaded = await LoadMapAsync(cancellationToken);

            _cache = loaded;
            if (Interlocked.Read(ref _cacheEpoch) != epochAtStart)
            {
                _cache = null;
            }

            return loaded;
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadMapAsync(CancellationToken cancellationToken)
    {
        const string sql = "SELECT source_code, iso_code FROM reference.country_alias";

        using var connection = await _systemConnectionFactory.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            map[(string)row.source_code] = (string)row.iso_code;
        }

        return map;
    }
}
