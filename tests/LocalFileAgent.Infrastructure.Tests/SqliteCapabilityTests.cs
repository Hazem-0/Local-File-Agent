using System;
using System.Globalization;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LocalFileAgent.Infrastructure.Tests;

public class SqliteCapabilityTests
{
    [Fact]
    public void Sqlite_SupportsWalMode()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        var result = command.ExecuteScalar()?.ToString();

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Sqlite_SupportsFts5Unicode61Tokenizer()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE VIRTUAL TABLE fts_content USING fts5(
                content,
                tokenize = 'unicode61'
            );
            INSERT INTO fts_content (content) VALUES ('تقرير مالي عن القاهرة');
            SELECT count(*) FROM fts_content WHERE fts_content MATCH 'القاهرة';
        ";

        var count = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        count.Should().Be(1);
    }

    [Fact]
    public void Sqlite_SupportsFts5TrigramTokenizer()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE VIRTUAL TABLE fts_names USING fts5(
                name,
                tokenize = 'trigram'
            );
            INSERT INTO fts_names (name) VALUES ('فاتورة_مبيعات_2025.pdf');
            SELECT count(*) FROM fts_names WHERE fts_names MATCH 'مبيعات';
        ";

        var count = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        count.Should().Be(1);
    }
}
