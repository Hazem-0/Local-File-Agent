using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using LocalFileAgent.Domain.Search;

namespace LocalFileAgent.Infrastructure.Storage;

public sealed class SqliteVectorIndex : IVectorIndex
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<long, float[]> _vectors = new();

    public SqliteVectorIndex(string databasePath)
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
                    CREATE TABLE IF NOT EXISTS chunk_vectors (
                        chunk_id INTEGER PRIMARY KEY,
                        dimensions INTEGER NOT NULL,
                        vector BLOB NOT NULL
                    );
                ";
                await schemaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                // Load existing vectors into in-memory SIMD cache
                using var selectCmd = _connection.CreateCommand();
                selectCmd.CommandText = "SELECT chunk_id, dimensions, vector FROM chunk_vectors;";

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

    public async Task AddAsync(IReadOnlyList<(long Id, float[] Vector)> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) return;

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

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
                    INSERT INTO chunk_vectors (chunk_id, dimensions, vector)
                    VALUES (@chunk_id, @dimensions, @vector)
                    ON CONFLICT(chunk_id) DO UPDATE SET
                        dimensions = excluded.dimensions,
                        vector = excluded.vector;
                ";
                cmd.Parameters.AddWithValue("@chunk_id", id);
                cmd.Parameters.AddWithValue("@dimensions", vector.Length);
                cmd.Parameters.AddWithValue("@vector", bytes);

                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                // Update in-memory cache
                _vectors[id] = vector;
            }

            transaction.Commit();
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<IReadOnlyList<(long Id, float Score)>> SearchAsync(
        float[] query,
        int k,
        Func<long, bool>? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (k <= 0 || query.Length == 0 || _vectors.IsEmpty)
        {
            return Task.FromResult<IReadOnlyList<(long Id, float Score)>>(Array.Empty<(long Id, float Score)>());
        }

        var results = new List<(long Id, float Score)>();
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

        return Task.FromResult<IReadOnlyList<(long Id, float Score)>>(results);
    }

    public Task<IReadOnlyList<(long Id, float Score)>> SearchExactAsync(
        float[] query,
        int k,
        IReadOnlySet<long> candidateIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidateIds);

        if (k <= 0 || query.Length == 0 || candidateIds.Count == 0 || _vectors.IsEmpty)
        {
            return Task.FromResult<IReadOnlyList<(long Id, float Score)>>(Array.Empty<(long Id, float Score)>());
        }

        var results = new List<(long Id, float Score)>(candidateIds.Count);
        var querySpan = query.AsSpan();

        foreach (var id in candidateIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_vectors.TryGetValue(id, out var vec))
            {
                continue;
            }

            if (vec.Length != query.Length)
            {
                continue;
            }

            var similarity = TensorPrimitives.CosineSimilarity(querySpan, vec.AsSpan());
            if (float.IsNaN(similarity))
            {
                similarity = 0f;
            }

            results.Add((id, similarity));
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));

        if (results.Count > k)
        {
            results = results.GetRange(0, k);
        }

        return Task.FromResult<IReadOnlyList<(long Id, float Score)>>(results);
    }

    public async Task DeleteAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return;

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var transaction = _connection!.BeginTransaction();

            foreach (var id in ids)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM chunk_vectors WHERE chunk_id = @chunk_id;";
                cmd.Parameters.AddWithValue("@chunk_id", id);
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

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "DELETE FROM chunk_vectors;";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _vectors.Clear();
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

        _vectors.Clear();
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
            throw new InvalidOperationException("SqliteVectorIndex is not initialized. Call InitializeAsync first.");
        }
    }
}
