using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Domain.Storage;

namespace LocalFileAgent.Infrastructure.Storage;

public sealed class SqliteIndexStore : IIndexStore
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SqliteIndexStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection == null)
            {
                _connection = new SqliteConnection(_connectionString);
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                // Enable WAL mode and foreign keys
                using var pragmaCmd = _connection.CreateCommand();
                pragmaCmd.CommandText = @"
                    PRAGMA journal_mode = WAL;
                    PRAGMA synchronous = NORMAL;
                    PRAGMA foreign_keys = ON;
                ";
                await pragmaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                // Create core tables, FTS5 virtual tables, and sync triggers
                using var schemaCmd = _connection.CreateCommand();
                schemaCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS files (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        path TEXT NOT NULL UNIQUE,
                        name TEXT NOT NULL,
                        extension TEXT NOT NULL,
                        size_bytes INTEGER NOT NULL,
                        created_at TEXT NOT NULL,
                        modified_at TEXT NOT NULL,
                        indexed_at TEXT NOT NULL,
                        etag TEXT NOT NULL,
                        status TEXT NOT NULL,
                        error_message TEXT
                    );

                    CREATE INDEX IF NOT EXISTS idx_files_path ON files(path);

                    CREATE TABLE IF NOT EXISTS chunks (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                        page_number INTEGER NOT NULL DEFAULT 1,
                        ordinal INTEGER NOT NULL DEFAULT 0,
                        text_raw TEXT NOT NULL,
                        text_normalized TEXT NOT NULL,
                        source_kind TEXT NOT NULL,
                        confidence REAL NOT NULL DEFAULT 1.0
                    );

                    CREATE INDEX IF NOT EXISTS idx_chunks_file_id ON chunks(file_id);

                    CREATE VIRTUAL TABLE IF NOT EXISTS fts_chunks_unicode61 USING fts5(
                        text_normalized,
                        content='chunks',
                        content_rowid='id'
                    );

                    CREATE VIRTUAL TABLE IF NOT EXISTS fts_chunks_trigram USING fts5(
                        text_normalized,
                        content='chunks',
                        content_rowid='id',
                        tokenize='trigram'
                    );

                    CREATE TRIGGER IF NOT EXISTS chunks_ai AFTER INSERT ON chunks BEGIN
                        INSERT INTO fts_chunks_unicode61(rowid, text_normalized) VALUES (new.id, new.text_normalized);
                        INSERT INTO fts_chunks_trigram(rowid, text_normalized) VALUES (new.id, new.text_normalized);
                    END;

                    CREATE TRIGGER IF NOT EXISTS chunks_ad AFTER DELETE ON chunks BEGIN
                        INSERT INTO fts_chunks_unicode61(fts_chunks_unicode61, rowid, text_normalized) VALUES('delete', old.id, old.text_normalized);
                        INSERT INTO fts_chunks_trigram(fts_chunks_trigram, rowid, text_normalized) VALUES('delete', old.id, old.text_normalized);
                    END;

                    CREATE TRIGGER IF NOT EXISTS chunks_au AFTER UPDATE ON chunks BEGIN
                        INSERT INTO fts_chunks_unicode61(fts_chunks_unicode61, rowid, text_normalized) VALUES('delete', old.id, old.text_normalized);
                        INSERT INTO fts_chunks_unicode61(rowid, text_normalized) VALUES (new.id, new.text_normalized);
                        INSERT INTO fts_chunks_trigram(fts_chunks_trigram, rowid, text_normalized) VALUES('delete', old.id, old.text_normalized);
                        INSERT INTO fts_chunks_trigram(rowid, text_normalized) VALUES (new.id, new.text_normalized);
                    END;
                ";
                await schemaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<long> UpsertFileAsync(IndexedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO files (path, name, extension, size_bytes, created_at, modified_at, indexed_at, etag, status, error_message)
                VALUES (@path, @name, @extension, @size_bytes, @created_at, @modified_at, @indexed_at, @etag, @status, @error_message)
                ON CONFLICT(path) DO UPDATE SET
                    name = excluded.name,
                    extension = excluded.extension,
                    size_bytes = excluded.size_bytes,
                    created_at = excluded.created_at,
                    modified_at = excluded.modified_at,
                    indexed_at = excluded.indexed_at,
                    etag = excluded.etag,
                    status = excluded.status,
                    error_message = excluded.error_message
                RETURNING id;
            ";

            cmd.Parameters.AddWithValue("@path", file.Path);
            cmd.Parameters.AddWithValue("@name", file.Name);
            cmd.Parameters.AddWithValue("@extension", file.Extension);
            cmd.Parameters.AddWithValue("@size_bytes", file.SizeBytes);
            cmd.Parameters.AddWithValue("@created_at", file.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@modified_at", file.ModifiedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@indexed_at", file.IndexedAt.ToString("O"));
            cmd.Parameters.AddWithValue("@etag", file.ETag);
            cmd.Parameters.AddWithValue("@status", file.Status);
            cmd.Parameters.AddWithValue("@error_message", (object?)file.ErrorMessage ?? DBNull.Value);

            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IndexedFile?> GetFileByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT id, path, name, extension, size_bytes, created_at, modified_at, indexed_at, etag, status, error_message
                FROM files
                WHERE path = @path;
            ";
            cmd.Parameters.AddWithValue("@path", path);

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new IndexedFile(
                    Id: reader.GetInt64(0),
                    Path: reader.GetString(1),
                    Name: reader.GetString(2),
                    Extension: reader.GetString(3),
                    SizeBytes: reader.GetInt64(4),
                    CreatedAt: DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture),
                    ModifiedAt: DateTimeOffset.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture),
                    IndexedAt: DateTimeOffset.Parse(reader.GetString(7), System.Globalization.CultureInfo.InvariantCulture),
                    ETag: reader.GetString(8),
                    Status: reader.GetString(9),
                    ErrorMessage: reader.IsDBNull(10) ? null : reader.GetString(10)
                );
            }

            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IndexedFile?> GetFileByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT id, path, name, extension, size_bytes, created_at, modified_at, indexed_at, etag, status, error_message
                FROM files
                WHERE id = @id;
            ";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new IndexedFile(
                    Id: reader.GetInt64(0),
                    Path: reader.GetString(1),
                    Name: reader.GetString(2),
                    Extension: reader.GetString(3),
                    SizeBytes: reader.GetInt64(4),
                    CreatedAt: DateTimeOffset.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture),
                    ModifiedAt: DateTimeOffset.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture),
                    IndexedAt: DateTimeOffset.Parse(reader.GetString(7), System.Globalization.CultureInfo.InvariantCulture),
                    ETag: reader.GetString(8),
                    Status: reader.GetString(9),
                    ErrorMessage: reader.IsDBNull(10) ? null : reader.GetString(10)
                );
            }

            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<long>> InsertChunksAsync(long fileId, IReadOnlyList<IndexedChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (chunks.Count == 0)
        {
            return Array.Empty<long>();
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var transaction = _connection!.BeginTransaction();

            // Clear previous chunks for this file
            using var deleteCmd = _connection.CreateCommand();
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM chunks WHERE file_id = @file_id;";
            deleteCmd.Parameters.AddWithValue("@file_id", fileId);
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            var chunkIds = new List<long>(chunks.Count);
            foreach (var chunk in chunks)
            {
                using var insertCmd = _connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"
                    INSERT INTO chunks (file_id, page_number, ordinal, text_raw, text_normalized, source_kind, confidence)
                    VALUES (@file_id, @page_number, @ordinal, @text_raw, @text_normalized, @source_kind, @confidence)
                    RETURNING id;
                ";
                insertCmd.Parameters.AddWithValue("@file_id", fileId);
                insertCmd.Parameters.AddWithValue("@page_number", chunk.PageNumber);
                insertCmd.Parameters.AddWithValue("@ordinal", chunk.Ordinal);
                insertCmd.Parameters.AddWithValue("@text_raw", chunk.TextRaw);
                insertCmd.Parameters.AddWithValue("@text_normalized", chunk.TextNormalized);
                insertCmd.Parameters.AddWithValue("@source_kind", chunk.SourceKind);
                insertCmd.Parameters.AddWithValue("@confidence", chunk.Confidence);

                var insertedId = await insertCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                chunkIds.Add(Convert.ToInt64(insertedId, System.Globalization.CultureInfo.InvariantCulture));
            }

            transaction.Commit();
            return chunkIds;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<SearchResultItem>> GetChunksByIdsAsync(
        IReadOnlyList<long> chunkIds,
        IReadOnlyList<string>? scopePaths = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (chunkIds == null || chunkIds.Count == 0)
        {
            return Array.Empty<SearchResultItem>();
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sb = new StringBuilder();
            sb.Append(@"
                SELECT c.id, f.path, f.name, c.page_number, c.source_kind, c.text_raw
                FROM chunks c
                JOIN files f ON f.id = c.file_id
                WHERE c.id IN (
            ");

            for (var i = 0; i < chunkIds.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"@id_{i}");
            }
            sb.Append(')');

            if (scopePaths is { Count: > 0 })
            {
                sb.Append(" AND (");
                for (var i = 0; i < scopePaths.Count; i++)
                {
                    if (i > 0) sb.Append(" OR ");
                    sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"f.path LIKE @scope_{i} || '%'");
                }
                sb.Append(')');
            }

            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = sb.ToString();

            for (var i = 0; i < chunkIds.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@id_{i}", chunkIds[i]);
            }

            if (scopePaths is { Count: > 0 })
            {
                for (var i = 0; i < scopePaths.Count; i++)
                {
                    cmd.Parameters.AddWithValue($"@scope_{i}", scopePaths[i]);
                }
            }

            var results = new List<SearchResultItem>();
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var chunkId = reader.GetInt64(0);
                var filePath = reader.GetString(1);
                var fileName = reader.GetString(2);
                var pageNumber = reader.GetInt32(3);
                var sourceKind = reader.GetString(4);
                var textRaw = reader.GetString(5);

                var snippet = textRaw.Length > 200 ? string.Concat(textRaw.AsSpan(0, 200), "...") : textRaw;

                results.Add(new SearchResultItem(
                    FilePath: filePath,
                    FileName: fileName,
                    PageNumber: pageNumber,
                    SourceKind: sourceKind,
                    Score: 0f,
                    Snippet: snippet,
                    ChunkId: chunkId,
                    MatchKind: "vector"
                ));
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteFileAsync(long fileId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "DELETE FROM files WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", fileId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM files;
                DELETE FROM chunks;
                INSERT INTO fts_chunks_unicode61(fts_chunks_unicode61) VALUES('delete-all');
                INSERT INTO fts_chunks_trigram(fts_chunks_trigram) VALUES('delete-all');
            ";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<SearchResultItem>> SearchFtsAsync(
        string normalizedQuery,
        IReadOnlyList<string>? scopePaths = null,
        int limit = 20,
        bool useTrigram = false,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return Array.Empty<SearchResultItem>();
        }

        if (useTrigram && normalizedQuery.Trim().Length < 3)
        {
            return Array.Empty<SearchResultItem>();
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ftsTable = useTrigram ? "fts_chunks_trigram" : "fts_chunks_unicode61";

            var sb = new StringBuilder();
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $@"
                SELECT f.path, f.name, c.page_number, c.source_kind, c.text_raw, bm25({ftsTable}) AS rank, c.id AS chunk_id
                FROM {ftsTable}
                JOIN chunks c ON c.id = {ftsTable}.rowid
                JOIN files f ON f.id = c.file_id
                WHERE {ftsTable} MATCH @query
            ");

            if (scopePaths is { Count: > 0 })
            {
                sb.Append(" AND (");
                for (var i = 0; i < scopePaths.Count; i++)
                {
                    if (i > 0) sb.Append(" OR ");
                    sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"f.path LIKE @scope_{i} || '%'");
                }
                sb.Append(')');
            }

            sb.Append(" ORDER BY rank LIMIT @limit;");

            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = sb.ToString();

            // Format FTS5 query terms: wrap each token in quotes for literal matching
            var formattedQuery = FormatFts5Query(normalizedQuery, useTrigram);
            cmd.Parameters.AddWithValue("@query", formattedQuery);
            cmd.Parameters.AddWithValue("@limit", limit);

            if (scopePaths is { Count: > 0 })
            {
                for (var i = 0; i < scopePaths.Count; i++)
                {
                    cmd.Parameters.AddWithValue($"@scope_{i}", scopePaths[i]);
                }
            }

            var results = new List<SearchResultItem>();
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var filePath = reader.GetString(0);
                var fileName = reader.GetString(1);
                var pageNumber = reader.GetInt32(2);
                var sourceKind = reader.GetString(3);
                var textRaw = reader.GetString(4);
                var rank = reader.GetDouble(5);
                var chunkId = reader.GetInt64(6);

                // Create a brief snippet from raw text
                var snippet = textRaw.Length > 200 ? string.Concat(textRaw.AsSpan(0, 200), "...") : textRaw;
                var score = (float)(1.0 / (1.0 + Math.Abs(rank))); // Normalize rank to [0, 1]

                results.Add(new SearchResultItem(
                    FilePath: filePath,
                    FileName: fileName,
                    PageNumber: pageNumber,
                    SourceKind: sourceKind,
                    Score: score,
                    Snippet: snippet,
                    ChunkId: chunkId,
                    MatchKind: "fts"
                ));
            }

            return results;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<long> GetIndexedFileCountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM files WHERE status = 'indexed';";
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<long> GetChunkCountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM chunks;";
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
        _lock.Dispose();
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_connection == null)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void EnsureInitialized()
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("SqliteIndexStore is not initialized. Call InitializeAsync first.");
        }
    }

    private static string FormatFts5Query(string query, bool isTrigram)
    {
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return "\"\"";

        if (isTrigram)
        {
            // Trigram requires tokens with length >= 3
            var trigramTokens = new List<string>();
            foreach (var t in tokens)
            {
                var clean = t.Replace("\"", "\"\"");
                if (clean.Length >= 3)
                {
                    trigramTokens.Add($"\"{clean}\"");
                }
            }
            if (trigramTokens.Count == 0)
            {
                return "\"\"";
            }
            return string.Join(" AND ", trigramTokens);
        }

        var ftsTokens = new List<string>();
        foreach (var t in tokens)
        {
            var clean = t.Replace("\"", "\"\"");
            if (!string.IsNullOrWhiteSpace(clean))
            {
                ftsTokens.Add($"\"{clean}\"");
            }
        }

        return ftsTokens.Count > 0 ? string.Join(" AND ", ftsTokens) : "\"\"";
    }
}
