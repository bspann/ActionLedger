using ActionLedger.Infrastructure.Persistence.Configurations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// The first migration, applied to an empty PostgreSQL 18. This is the story's schema row: the
/// conventions in <c>AppDbContext</c> are only real if they reached the database.
/// </summary>
[Collection(PostgresFixture.CollectionName)]
public sealed class SchemaShapeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_first_migration_creates_users_with_snake_case_columns()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.CreateDatabaseAsync(nameof(The_first_migration_creates_users_with_snake_case_columns), cancellationToken);

        await using ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff);
        await TestHost.MigrateAsync(services, cancellationToken);

        Dictionary<string, string> columns = await ColumnsAsync(connectionString, "users", cancellationToken);

        Assert.Equal(
            ["created_at", "display_name", "id", "is_system", "password_hash", "role", "username"],
            columns.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Instants_are_timestamptz_and_the_key_is_a_uuid()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.CreateDatabaseAsync(nameof(Instants_are_timestamptz_and_the_key_is_a_uuid), cancellationToken);

        await using ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff);
        await TestHost.MigrateAsync(services, cancellationToken);

        Dictionary<string, string> columns = await ColumnsAsync(connectionString, "users", cancellationToken);

        Assert.Equal("uuid", columns["id"]);
        Assert.Equal("timestamp with time zone", columns["created_at"]);

        // AD-10, Enums row: the role is stored as a string, not as an ordinal — so a reordered
        // enum can never silently repoint existing rows.
        Assert.Equal("character varying", columns["role"]);
    }

    [Fact]
    public async Task Username_carries_the_AD20_unique_index()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.CreateDatabaseAsync(nameof(Username_carries_the_AD20_unique_index), cancellationToken);

        await using ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff);
        await TestHost.MigrateAsync(services, cancellationToken);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'users' AND indexname = @name
            """,
            connection);

        command.Parameters.AddWithValue("name", UserConfiguration.UsernameIndexName);

        string? definition = (string?)await command.ExecuteScalarAsync(cancellationToken);

        Assert.NotNull(definition);
        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains("username", definition, StringComparison.Ordinal);
    }

    private static async Task<Dictionary<string, string>> ColumnsAsync(
        string connectionString,
        string table,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> columns = new(StringComparer.Ordinal);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using NpgsqlCommand command = new(
            """
            SELECT column_name, data_type
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @table
            """,
            connection);

        command.Parameters.AddWithValue("table", table);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            columns[reader.GetString(0)] = reader.GetString(1);
        }

        return columns;
    }
}
