using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Users;
using ActionLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// AD-10 and AD-20 against a real database: the Domain's id is the row's id, a new aggregate is
/// written with inserts and nothing else, and a unique-index violation leaves this ring as
/// <see cref="ConcurrencyConflictException"/> rather than as an EF Core or Npgsql type.
/// </summary>
[Collection(PostgresFixture.CollectionName)]
public sealed class PersistenceBehaviourTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_persisted_id_is_the_uuidv7_the_domain_constructor_made()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(The_persisted_id_is_the_uuidv7_the_domain_constructor_made), cancellationToken);

        User user = NewUser("dana");
        Guid expected = user.Id;

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IUserRepository>().Add(user);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        }

        await using AsyncServiceScope read = services.CreateAsyncScope();
        User? stored = await read.ServiceProvider.GetRequiredService<IUserRepository>()
            .FindByUsernameAsync("dana", cancellationToken);

        Assert.NotNull(stored);
        Assert.Equal(expected, stored.Id);

        // Version 7: the nibble the UUIDv7 layout puts in byte 6. EF generated nothing, or this
        // would be a database-side value with a different version.
        Assert.Equal(7, (stored.Id.ToByteArray(bigEndian: true)[6] & 0xF0) >> 4);
    }

    [Fact]
    public async Task A_new_aggregate_persists_as_inserts_only()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RecordingCommandInterceptor recorder = new();

        await using ServiceProvider services = await MigratedHostAsync(
            nameof(A_new_aggregate_persists_as_inserts_only),
            cancellationToken,
            // Layered onto the production registration rather than replacing it, so what the
            // recorder sees is what AddActionLedgerPersistence configured.
            collection => collection.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(recorder)));

        // Migrating is a wall of DDL. Only what the write itself sends is of interest.
        recorder.Clear();

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IUserRepository>().Add(NewUser("priya"));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        }

        string[] touchingUsers =
        [
            .. recorder.Commands.Where(command => command.Contains("users", StringComparison.OrdinalIgnoreCase)),
        ];

        Assert.NotEmpty(touchingUsers);
        Assert.Contains(touchingUsers, command => command.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(touchingUsers, command => command.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_duplicate_username_surfaces_as_a_concurrency_conflict()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(nameof(A_duplicate_username_surfaces_as_a_concurrency_conflict), cancellationToken);

        await using (AsyncServiceScope first = services.CreateAsyncScope())
        {
            first.ServiceProvider.GetRequiredService<IUserRepository>().Add(NewUser("marcus"));
            await first.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        }

        await using AsyncServiceScope second = services.CreateAsyncScope();
        second.ServiceProvider.GetRequiredService<IUserRepository>().Add(NewUser("MARCUS"));

        ConcurrencyConflictException conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => second.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken));

        // AD-1: no EF Core or Npgsql type may reach Application. The provider's exception is the
        // inner one, and it stops here.
        Assert.IsAssignableFrom<DbUpdateException>(conflict.InnerException);
    }

    private async Task<ServiceProvider> MigratedHostAsync(
        string name,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configure = null)
    {
        string connectionString = await postgres.CreateDatabaseAsync(name, cancellationToken);

        ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff, configure);

        await TestHost.MigrateAsync(services, cancellationToken);

        return services;
    }

    private static User NewUser(string username) =>
        User.Register(username, $"Test {username}", "not-a-real-hash", Role.ActionOfficer, DateTimeOffset.UnixEpoch);
}
