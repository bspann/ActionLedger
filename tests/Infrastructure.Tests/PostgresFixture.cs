using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// AD-18 — one real PostgreSQL 18 for the whole assembly. Every test gets its own database on it,
/// so migrations are applied from empty each time and no test can see another's rows.
/// </summary>
/// <remarks>
/// The image matches the compose service (<c>postgres:18-alpine</c>), so what the migration is
/// proven against is what the demo runs on. The first run on a machine pulls the image.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("actionledger")
        .WithUsername("actionledger")
        .WithPassword("actionledger-tests-not-a-secret")
        .Build();

    /// <summary>xunit's collection name, so every test class shares one container.</summary>
    public const string CollectionName = "postgres";

    /// <summary>
    /// Creates an empty database and returns its connection string. The name is derived from the
    /// caller so a failure names the test that owns the rows.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(string name, CancellationToken cancellationToken = default)
    {
        string database = Sanitize(name);

        // Quoted, and the name is sanitized to lower-case letters, digits, and underscores —
        // CREATE DATABASE takes no parameters, so there is nothing to bind.
        await _container.ExecScriptAsync($"CREATE DATABASE \"{database}\"", cancellationToken);

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = database,

            // A probe against a database that is not there should fail in about a second, not
            // sit on the default connect timeout.
            Timeout = 2,
        }.ConnectionString;
    }

    async ValueTask IAsyncLifetime.InitializeAsync() => await _container.StartAsync(TestContext.Current.CancellationToken);

    async ValueTask IAsyncDisposable.DisposeAsync() => await _container.DisposeAsync();

    private static string Sanitize(string name)
    {
        string lowered = new([.. name.Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_')]);

        return lowered.Length <= 40 ? lowered : lowered[..40];
    }
}

/// <summary>Binds every database test class to the one container.</summary>
[CollectionDefinition(PostgresFixture.CollectionName)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
