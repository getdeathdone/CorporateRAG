using CorporateRag.Vector;
using Microsoft.Data.Sqlite;
using System.Runtime.InteropServices;

namespace CorporateRag.Cache;

public sealed class SqliteRagCacheStore : IRagCacheStore
{
    private readonly string _dbPath;
    private readonly object _gate = new();

    public SqliteRagCacheStore()
    {
        var dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CorporateRag",
            "data");
        Directory.CreateDirectory(dataPath);
        _dbPath = Path.Combine(dataPath, "rag-cache.db");
        EnsureCreated();
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "select count(*) from chunks;";
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }
    }

    public IReadOnlyList<(RagChunk Chunk, ReadOnlyMemory<float> Embedding)> Load()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                select id, source, chunk_index, text, embedding
                from chunks
                order by rowid;
                """;

            using var reader = command.ExecuteReader();
            var items = new List<(RagChunk Chunk, ReadOnlyMemory<float> Embedding)>();

            while (reader.Read())
            {
                var chunk = new RagChunk(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetString(3));

                var bytes = (byte[])reader["embedding"];
                items.Add((chunk, BytesToFloats(bytes).AsMemory()));
            }

            return items;
        }
    }

    public void Save(IEnumerable<(RagChunk Chunk, ReadOnlyMemory<float> Embedding)> items)
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "delete from chunks;";
                delete.ExecuteNonQuery();
            }

            foreach (var item in items)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    insert into chunks (id, source, chunk_index, text, embedding)
                    values ($id, $source, $chunk_index, $text, $embedding);
                    """;
                insert.Parameters.AddWithValue("$id", item.Chunk.Id);
                insert.Parameters.AddWithValue("$source", item.Chunk.Source);
                insert.Parameters.AddWithValue("$chunk_index", item.Chunk.Index);
                insert.Parameters.AddWithValue("$text", item.Chunk.Text);
                insert.Parameters.Add("$embedding", SqliteType.Blob).Value = FloatsToBytes(item.Embedding.Span);
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "delete from chunks;";
            command.ExecuteNonQuery();
        }
    }

    private void EnsureCreated()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                create table if not exists chunks (
                    id text not null,
                    source text not null,
                    chunk_index integer not null,
                    text text not null,
                    embedding blob not null
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    private static byte[] FloatsToBytes(ReadOnlySpan<float> values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        MemoryMarshal.Cast<float, byte>(values).CopyTo(bytes);
        return bytes;
    }

    private static float[] BytesToFloats(byte[] bytes)
    {
        var values = new float[bytes.Length / sizeof(float)];
        MemoryMarshal.Cast<byte, float>(bytes).CopyTo(values);
        return values;
    }
}
