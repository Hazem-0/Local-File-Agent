using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using LocalFileAgent.Domain.Search;

namespace LocalFileAgent.Infrastructure.Storage;

public sealed class SqliteVisualVectorIndex : IVisualVectorIndex
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<long, float[]> _vectors = new();

    public SqliteVisualVectorIndex(string databasePath)
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

                using var schemaCmd = _connection.CreateCommand();
                schemaCmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS file_visual_vectors (
                        file_id INTEGER PRIMARY KEY,
                        dimensions INTEGER NOT NULL,
                        vector BLOB NOT NULL
                    );
                ";
                await schemaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                using var selectCmd = _connection.CreateCommand();
                selectCmd.CommandText = "SELECT file_id, dimensions, vector FROM file_visual_vectors;";

                using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var id = reader.GetInt64(0);
                    var dims = reader.GetInt32(1);
                    var bytes = (byte[])reader.GetValue(2);

                    if (bytes.Length == dims * sizeof(float))
                    {
                        var floatSpan = MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
                        _vectors[id] = floatSpan.ToArray();
                    }
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddAsync(IReadOnlyList<(long FileId, float[] Vector)> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) return;

        EnsureInitialized();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var transaction = _connection!.BeginTransaction();

            foreach (var (id, vector) in items)
            {
                if (vector == null || vector.Length == 0) continue;

                var bytes = MemoryMarshal.AsBytes<float>(vector.AsSpan()).ToArray();

                using var cmd = _connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO file_visual_vectors (file_id, dimensions, vector)
                    VALUES (@file_id, @dimensions, @vector)
                    ON CONFLICT(file_id) DO UPDATE SET
                        dimensions = excluded.dimensions,
                        vector = excluded.vector;
                ";
                cmd.Parameters.AddWithValue("@file_id", id);
                cmd.Parameters.AddWithValue("@dimensions", vector.Length);
                cmd.Parameters.AddWithValue("@vector", bytes);

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                _vectors[id] = vector;
            }

            transaction.Commit();
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<IReadOnlyList<(long FileId, float Score)>> SearchAsync(
        float[] query,
        int k,
        Func<long, bool>? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (k <= 0 || query.Length == 0 || _vectors.IsEmpty)
        {
            return Task.FromResult<IReadOnlyList<(long FileId, float Score)>>(Array.Empty<(long FileId, float Score)>());
        }

        var results = new List<(long FileId, float Score)>();
        var querySpan = query.AsSpan();

        foreach (var kvp in _vectors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (filter != null && !filter(kvp.Key))
            {
                continue;
            }

            var vec = kvp.Value;
            if (vec.Length != query.Length)
            {
                continue;
            }

            var similarity = TensorPrimitives.CosineSimilarity(querySpan, vec.AsSpan());
            if (float.IsNaN(similarity))
            {
                similarity = 0f;
            }

            results.Add((kvp.Key, similarity));
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));

        if (results.Count > k)
        {
            results = results.GetRange(0, k);
        }

        return Task.FromResult<IReadOnlyList<(long FileId, float Score)>>(results);
    }

    public async Task DeleteAsync(IReadOnlyList<long> fileIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        if (fileIds.Count == 0) return;

        EnsureInitialized();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var transaction = _connection!.BeginTransaction();

            foreach (var id in fileIds)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM file_visual_vectors WHERE file_id = @file_id;";
                cmd.Parameters.AddWithValue("@file_id", id);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                _vectors.TryRemove(id, out _);
            }

            transaction.Commit();
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_vectors.Count);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        _vectors.Clear();
        _lock.Dispose();
    }

    private void EnsureInitialized()
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("SqliteVisualVectorIndex is not initialized. Call InitializeAsync first.");
        }
    }
}
