namespace Liakont.Agent.Core.Storage;

using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Liakont.Agent.Core.Time;

/// <summary>
/// File locale de l'agent (F12 §2.3) : un TAMPON TECHNIQUE en SQLite (mode WAL), PAS une piste
/// d'audit (l'audit légal vit sur la plateforme). Trois tables :
/// <list type="bullet">
///   <item><c>push_queue</c> — éléments à pousser ; purgés UNIQUEMENT après ACK terminal (AGT02).</item>
///   <item><c>pushed_log</c> — (kind, source_reference, payload_hash, date ACK) anti re-push ;
///   rétention <see cref="PushedLogRetentionDays"/> jours, purgée automatiquement.</item>
///   <item><c>agent_state</c> — filigrane d'extraction + dernière config ; JAMAIS purgé automatiquement.</item>
/// </list>
/// <para>
/// Concurrence — deux niveaux :
/// <list type="bullet">
///   <item>INTER-process (service LocalSystem + CLI intégrateur) : chaque processus ouvre sa propre
///   connexion ; WAL autorise lecteurs concurrents + un rédacteur, <c>busy_timeout</c> absorbe la
///   contention. La sérialisation des RUNS est portée par le mutex nommé
///   (<see cref="Hosting.InterProcessRunLock"/>), pas par cette base.</item>
///   <item>INTRA-process : une instance détient une UNIQUE connexion ; tous les accès sont
///   sérialisés par un verrou interne, de sorte que plusieurs threads du même processus (p. ex. la
///   boucle de run et le thread de heartbeat introduits par AGT02/AGT03) peuvent l'utiliser sans
///   corrompre l'état ADO.NET.</item>
/// </list>
/// </para>
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "LocalQueue est le nom métier de la file de push locale (F12 §2.3) — ce n'est pas un type de collection du framework.")]
public sealed class LocalQueue : IDisposable
{
    /// <summary>Rétention de <c>pushed_log</c> (anti re-push), en jours (F12 §2.3).</summary>
    public const int PushedLogRetentionDays = 90;

    /// <summary>Clé de filigrane d'extraction dans <c>agent_state</c>.</summary>
    public const string ExtractionWatermarkKey = "extraction.watermark.utc";

    /// <summary>Clé de la dernière configuration reçue de la plateforme dans <c>agent_state</c>.</summary>
    public const string LastConfigurationKey = "platform.last_configuration";

    /// <summary>Clé des régimes de TVA source collectés au dernier run, à joindre au prochain push (TVA03).</summary>
    public const string SourceTaxRegimesKey = "push.source_tax_regimes";

    // Sérialise tous les accès à l'unique connexion (sûreté intra-process). Réentrant : un appelant
    // public qui en délègue un autre (MarkError → SetStatus) ne se bloque pas lui-même.
    private readonly object _operationLock = new object();
    private readonly IClock _clock;
    private readonly SQLiteConnection _connection;

    public LocalQueue(string databasePath, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Le chemin de la base locale est requis.", nameof(databasePath));
        }

        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Pooling=False : une seule connexion longue durée par processus ; évite qu'un handle resté
        // dans le pool ne verrouille le fichier (notamment au nettoyage des bases de test).
        string connectionString = new SQLiteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
            BusyTimeout = 5000,
            JournalMode = SQLiteJournalModeEnum.Wal,
            DateTimeKind = DateTimeKind.Utc,
        }.ToString();

        _connection = new SQLiteConnection(connectionString);
        _connection.Open();
        EnsureSchema();
    }

    /// <summary>Enfile un élément. Idempotent : un élément de même (kind, source_reference, payload_hash) n'est pas dupliqué.</summary>
    public EnqueueResult Enqueue(QueueItem item)
    {
        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        lock (_operationLock)
        {
            string now = FormatUtc(_clock.UtcNow);
            using (SQLiteCommand cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT OR IGNORE INTO push_queue " +
                    "(kind, source_reference, payload_hash, payload_json, file_path, status, attempts, last_error, created_at_utc, updated_at_utc) " +
                    "VALUES (@kind, @sref, @hash, @payload, @file, @status, 0, NULL, @created, @updated);";
                cmd.Parameters.AddWithValue("@kind", item.Kind.ToString());
                cmd.Parameters.AddWithValue("@sref", item.SourceReference);
                cmd.Parameters.AddWithValue("@hash", (object?)item.PayloadHash ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@payload", (object?)item.PayloadJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@file", (object?)item.FilePath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@status", QueueItemStatus.Pending.ToString());
                cmd.Parameters.AddWithValue("@created", now);
                cmd.Parameters.AddWithValue("@updated", now);
                int affected = cmd.ExecuteNonQuery();
                return affected > 0 ? EnqueueResult.Enqueued : EnqueueResult.AlreadyQueued;
            }
        }
    }

    /// <summary>Renvoie les éléments à pousser (Pending puis Error, ordre d'arrivée), au plus <paramref name="max"/>.</summary>
    public IReadOnlyList<QueuedItem> PeekPending(int max)
    {
        if (max <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Le nombre d'éléments demandé doit être positif.");
        }

        lock (_operationLock)
        {
            var result = new List<QueuedItem>();
            using (SQLiteCommand cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT id, kind, source_reference, payload_hash, payload_json, file_path, status, attempts, last_error, created_at_utc, updated_at_utc " +
                    "FROM push_queue WHERE status IN (@pending, @error) ORDER BY id LIMIT @max;";
                cmd.Parameters.AddWithValue("@pending", QueueItemStatus.Pending.ToString());
                cmd.Parameters.AddWithValue("@error", QueueItemStatus.Error.ToString());
                cmd.Parameters.AddWithValue("@max", max);
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(ReadQueuedItem(reader));
                    }
                }
            }

            return result;
        }
    }

    /// <summary>Renvoie le nombre d'éléments par statut en une seule requête SQL (SELECT GROUP BY).</summary>
    public IReadOnlyDictionary<QueueItemStatus, int> CountByStatus()
    {
        lock (_operationLock)
        {
            var result = new Dictionary<QueueItemStatus, int>();
            using (SQLiteCommand cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT status, COUNT(*) FROM push_queue GROUP BY status;";
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var status = (QueueItemStatus)Enum.Parse(typeof(QueueItemStatus), reader.GetString(0));
                        result[status] = reader.GetInt32(1);
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Renvoie les éléments d'un statut donné (et, en option, d'un type donné), par ordre d'arrivée,
    /// au plus <paramref name="max"/>. Utilisé par le drainage (AGT02) pour sélectionner précisément
    /// les documents à pousser (Pending) ou à réconcilier (InProgress) sans mélanger les types.
    /// </summary>
    /// <param name="status">Statut recherché.</param>
    /// <param name="kind">Type recherché, ou <c>null</c> pour tous les types.</param>
    /// <param name="max">Nombre maximum d'éléments à renvoyer (strictement positif).</param>
    /// <returns>Les éléments correspondants, par ordre d'arrivée.</returns>
    public IReadOnlyList<QueuedItem> Peek(QueueItemStatus status, QueueItemKind? kind, int max)
    {
        if (max <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Le nombre d'éléments demandé doit être positif.");
        }

        lock (_operationLock)
        {
            var result = new List<QueuedItem>();
            using (SQLiteCommand cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT id, kind, source_reference, payload_hash, payload_json, file_path, status, attempts, last_error, created_at_utc, updated_at_utc " +
                    "FROM push_queue WHERE status = @status" +
                    (kind.HasValue ? " AND kind = @kind" : string.Empty) +
                    " ORDER BY id LIMIT @max;";
                cmd.Parameters.AddWithValue("@status", status.ToString());
                if (kind.HasValue)
                {
                    cmd.Parameters.AddWithValue("@kind", kind.Value.ToString());
                }

                cmd.Parameters.AddWithValue("@max", max);
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Add(ReadQueuedItem(reader));
                    }
                }
            }

            return result;
        }
    }

    /// <summary>Compte les éléments présents dans la file (tous statuts).</summary>
    public int Count()
    {
        lock (_operationLock)
        {
            using (SQLiteCommand cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM push_queue;";
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Marque un élément « en cours » (push émis, ACK terminal en attente — ADR-0012).</summary>
    public void MarkInProgress(long id) => SetStatus(id, QueueItemStatus.InProgress, error: null, incrementAttempts: false);

    /// <summary>
    /// Remet un élément « en attente » (renvoi au prochain push). Utilisé par la réconciliation ADR-0012
    /// quand un élément « en cours » est rapporté non terminal (reçu mais non rangé).
    /// </summary>
    public void MarkPending(long id) => SetStatus(id, QueueItemStatus.Pending, error: null, incrementAttempts: false);

    /// <summary>Marque un élément en erreur (incrémente le compteur de tentatives, mémorise le motif).</summary>
    public void MarkError(long id, string error) => SetStatus(id, QueueItemStatus.Error, error, incrementAttempts: true);

    /// <summary>
    /// Acquitte un élément : le déplace de <c>push_queue</c> vers <c>pushed_log</c> en une transaction.
    /// Un acquittement n'a de sens que pour un élément porteur d'une empreinte (document) — un PDF
    /// sans empreinte est simplement retiré de la file.
    /// </summary>
    public void Acknowledge(long id)
    {
        lock (_operationLock)
        {
            using (SQLiteTransaction tx = _connection.BeginTransaction())
            {
                string kind;
                string sourceReference;
                string? payloadHash;
                using (SQLiteCommand read = _connection.CreateCommand())
                {
                    read.Transaction = tx;
                    read.CommandText = "SELECT kind, source_reference, payload_hash FROM push_queue WHERE id = @id;";
                    read.Parameters.AddWithValue("@id", id);
                    using (SQLiteDataReader reader = read.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            tx.Rollback();
                            return;
                        }

                        kind = reader.GetString(0);
                        sourceReference = reader.GetString(1);
                        payloadHash = reader.IsDBNull(2) ? null : reader.GetString(2);
                    }
                }

                if (payloadHash != null)
                {
                    using (SQLiteCommand insert = _connection.CreateCommand())
                    {
                        insert.Transaction = tx;
                        insert.CommandText =
                            "INSERT OR REPLACE INTO pushed_log (kind, source_reference, payload_hash, acked_at_utc) " +
                            "VALUES (@kind, @sref, @hash, @acked);";
                        insert.Parameters.AddWithValue("@kind", kind);
                        insert.Parameters.AddWithValue("@sref", sourceReference);
                        insert.Parameters.AddWithValue("@hash", payloadHash);
                        insert.Parameters.AddWithValue("@acked", FormatUtc(_clock.UtcNow));
                        insert.ExecuteNonQuery();
                    }
                }

                using (SQLiteCommand delete = _connection.CreateCommand())
                {
                    delete.Transaction = tx;
                    delete.CommandText = "DELETE FROM push_queue WHERE id = @id;";
                    delete.Parameters.AddWithValue("@id", id);
                    delete.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }
    }

    /// <summary>Indique si un document de cette clé a déjà été acquitté (anti re-push, F12 §2.2).</summary>
    public bool IsAlreadyPushed(QueueItemKind kind, string sourceReference, string payloadHash)
    {
        lock (_operationLock)
        {
            using (SQLiteCommand cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT 1 FROM pushed_log WHERE kind = @kind AND source_reference = @sref AND payload_hash = @hash LIMIT 1;";
                cmd.Parameters.AddWithValue("@kind", kind.ToString());
                cmd.Parameters.AddWithValue("@sref", sourceReference);
                cmd.Parameters.AddWithValue("@hash", payloadHash);
                return cmd.ExecuteScalar() != null;
            }
        }
    }

    /// <summary>
    /// Purge les entrées de <c>pushed_log</c> plus anciennes que la rétention (F12 §2.3).
    /// N'affecte JAMAIS <c>push_queue</c> ni <c>agent_state</c>. Renvoie le nombre d'entrées supprimées.
    /// </summary>
    public int PurgeExpiredPushedLog()
    {
        lock (_operationLock)
        {
            DateTime cutoff = _clock.UtcNow.AddDays(-PushedLogRetentionDays);
            using (SQLiteCommand cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM pushed_log WHERE acked_at_utc < @cutoff;";
                cmd.Parameters.AddWithValue("@cutoff", FormatUtc(cutoff));
                return cmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>Lit une valeur de <c>agent_state</c> (null si absente).</summary>
    public string? GetState(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("La clé d'état est requise.", nameof(key));
        }

        lock (_operationLock)
        {
            using (SQLiteCommand cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT value FROM agent_state WHERE key = @key;";
                cmd.Parameters.AddWithValue("@key", key);
                object? value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? null : (string)value;
            }
        }
    }

    /// <summary>Écrit (ou remplace) une valeur de <c>agent_state</c>.</summary>
    public void SetState(string key, string? value)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("La clé d'état est requise.", nameof(key));
        }

        lock (_operationLock)
        {
            using (SQLiteCommand cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "INSERT INTO agent_state (key, value, updated_at_utc) VALUES (@key, @value, @updated) " +
                    "ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at_utc = excluded.updated_at_utc;";
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", (object?)value ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@updated", FormatUtc(_clock.UtcNow));
                cmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>Lit le filigrane d'extraction (dernière période traitée), null si jamais posé.</summary>
    public DateTime? GetExtractionWatermarkUtc()
    {
        string? raw = GetState(ExtractionWatermarkKey);
        if (raw == null)
        {
            return null;
        }

        return DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
    }

    /// <summary>Pose le filigrane d'extraction.</summary>
    public void SetExtractionWatermarkUtc(DateTime watermarkUtc) =>
        SetState(ExtractionWatermarkKey, FormatUtc(watermarkUtc));

    /// <summary>Renvoie le mode de journalisation effectif de SQLite (« wal » attendu) — utilitaire de diagnostic/test.</summary>
    public string GetJournalMode()
    {
        lock (_operationLock)
        {
            using (SQLiteCommand cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode;";
                return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }
    }

    public void Dispose()
    {
        lock (_operationLock)
        {
            _connection.Dispose();
        }
    }

    private static QueuedItem ReadQueuedItem(SQLiteDataReader reader)
    {
        return new QueuedItem(
            id: reader.GetInt64(0),
            kind: (QueueItemKind)Enum.Parse(typeof(QueueItemKind), reader.GetString(1)),
            sourceReference: reader.GetString(2),
            payloadHash: reader.IsDBNull(3) ? null : reader.GetString(3),
            payloadJson: reader.IsDBNull(4) ? null : reader.GetString(4),
            filePath: reader.IsDBNull(5) ? null : reader.GetString(5),
            status: (QueueItemStatus)Enum.Parse(typeof(QueueItemStatus), reader.GetString(6)),
            attempts: reader.GetInt32(7),
            lastError: reader.IsDBNull(8) ? null : reader.GetString(8),
            createdAtUtc: ParseUtc(reader.GetString(9)),
            updatedAtUtc: ParseUtc(reader.GetString(10)));
    }

    private static string FormatUtc(DateTime value) =>
        value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTime ParseUtc(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private void EnsureSchema()
    {
        using (SQLiteCommand cmd = _connection.CreateCommand())
        {
            cmd.CommandText =
                "CREATE TABLE IF NOT EXISTS push_queue (" +
                "  id INTEGER PRIMARY KEY AUTOINCREMENT," +
                "  kind TEXT NOT NULL," +
                "  source_reference TEXT NOT NULL," +
                "  payload_hash TEXT NULL," +
                "  payload_json TEXT NULL," +
                "  file_path TEXT NULL," +
                "  status TEXT NOT NULL," +
                "  attempts INTEGER NOT NULL DEFAULT 0," +
                "  last_error TEXT NULL," +
                "  created_at_utc TEXT NOT NULL," +
                "  updated_at_utc TEXT NOT NULL" +
                ");" +
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_push_queue_key " +
                "  ON push_queue (kind, source_reference, IFNULL(payload_hash, ''));" +
                "CREATE TABLE IF NOT EXISTS pushed_log (" +
                "  kind TEXT NOT NULL," +
                "  source_reference TEXT NOT NULL," +
                "  payload_hash TEXT NOT NULL," +
                "  acked_at_utc TEXT NOT NULL," +
                "  PRIMARY KEY (kind, source_reference, payload_hash)" +
                ");" +
                "CREATE INDEX IF NOT EXISTS ix_pushed_log_acked ON pushed_log (acked_at_utc);" +
                "CREATE TABLE IF NOT EXISTS agent_state (" +
                "  key TEXT PRIMARY KEY," +
                "  value TEXT NULL," +
                "  updated_at_utc TEXT NOT NULL" +
                ");";
            cmd.ExecuteNonQuery();
        }
    }

    private void SetStatus(long id, QueueItemStatus status, string? error, bool incrementAttempts)
    {
        lock (_operationLock)
        {
            using (SQLiteCommand cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "UPDATE push_queue SET status = @status, updated_at_utc = @updated" +
                    (incrementAttempts ? ", attempts = attempts + 1" : string.Empty) +
                    ", last_error = @error WHERE id = @id;";
                cmd.Parameters.AddWithValue("@status", status.ToString());
                cmd.Parameters.AddWithValue("@updated", FormatUtc(_clock.UtcNow));
                cmd.Parameters.AddWithValue("@error", (object?)error ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }
    }
}
