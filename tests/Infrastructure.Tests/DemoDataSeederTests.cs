using ActionLedger.Domain.Users;
using ActionLedger.Infrastructure.Auth;
using ActionLedger.Infrastructure.Persistence;
using ActionLedger.Infrastructure.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// AD-21 — the seeder writes the four demo users once, through aggregate methods, and a second
/// start changes nothing. Asserted against a real PostgreSQL, because "nothing changed" is a
/// claim about rows, not about a mock.
/// </summary>
[Collection(PostgresFixture.CollectionName)]
public sealed class DemoDataSeederTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Seeding_creates_the_three_people_and_the_system_user()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(
            nameof(Seeding_creates_the_three_people_and_the_system_user), TestHost.SeedingOn(), cancellationToken);

        await TestHost.StartSeederAsync(services, cancellationToken);

        List<User> users = await AllUsersAsync(services, cancellationToken);

        Assert.Equal(4, users.Count);

        Assert.Equal(
            ["dana", "marcus", "priya", "seed"],
            users.Select(user => user.Username).Order(StringComparer.Ordinal));

        Assert.Equal(Role.ActionOfficer, Single(users, "dana").Role);
        Assert.Equal("Dana Whitfield", Single(users, "dana").DisplayName);
        Assert.Equal(Role.ActionOfficer, Single(users, "priya").Role);
        Assert.Equal("Priya Ramaswamy", Single(users, "priya").DisplayName);
        Assert.Equal(Role.Lead, Single(users, "marcus").Role);
        Assert.Equal("Marcus Bell", Single(users, "marcus").DisplayName);

        // Exactly one system User, and it is the one seeded data is attributed to.
        Assert.Equal(["seed"], users.Where(user => user.IsSystem).Select(user => user.Username));
        Assert.Equal(DemoDataSeeder.SystemDisplayName, Single(users, "seed").DisplayName);
    }

    [Fact]
    public async Task The_three_people_sign_in_with_the_configured_password_and_the_system_user_cannot()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // The password is made up here and exists only in this process. There is no constant to
        // agree with, so what the assertion proves is that the seeder used what it was given.
        SeedSettings seeding = TestHost.SeedingOn();

        await using ServiceProvider services = await MigratedHostAsync(
            nameof(The_three_people_sign_in_with_the_configured_password_and_the_system_user_cannot), seeding, cancellationToken);

        await TestHost.StartSeederAsync(services, cancellationToken);

        List<User> users = await AllUsersAsync(services, cancellationToken);
        PasswordService passwords = services.GetRequiredService<PasswordService>();

        foreach (DemoDataSeeder.DemoUser demo in DemoDataSeeder.DemoUsers)
        {
            User user = Single(users, demo.Username);

            // A hash, not the password: the credential must not be recoverable from the row.
            Assert.DoesNotContain(seeding.DefaultPassword, user.PasswordHash, StringComparison.Ordinal);
            Assert.Equal(PasswordVerificationResult.Success, passwords.Verify(user, seeding.DefaultPassword));
        }

        // The Seed User is an attribution identity, not an account. Nobody holds its password.
        Assert.Equal(
            PasswordVerificationResult.Failed,
            passwords.Verify(Single(users, DemoDataSeeder.SystemUsername), seeding.DefaultPassword));
    }

    [Fact]
    public async Task Seeding_on_without_a_password_refuses_rather_than_inventing_one()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using ServiceProvider services = await MigratedHostAsync(
            nameof(Seeding_on_without_a_password_refuses_rather_than_inventing_one),
            new SeedSettings(Enabled: true, DefaultPassword: ""),
            cancellationToken);

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => TestHost.StartSeederAsync(services, cancellationToken));

        Assert.Contains("Seed:DefaultPassword", refusal.Message, StringComparison.Ordinal);

        // NFR5, stated as a row rather than as a convention: no default credential exists to fall
        // back to, so nothing was created.
        Assert.Empty(await AllUsersAsync(services, cancellationToken));
    }

    [Fact]
    public async Task A_second_start_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(
            nameof(A_second_start_changes_nothing), TestHost.SeedingOn(), cancellationToken);

        await TestHost.StartSeederAsync(services, cancellationToken);

        (Guid Id, string Username, string PasswordHash, DateTimeOffset CreatedAt)[] before = Fingerprint(await AllUsersAsync(services, cancellationToken));

        await TestHost.StartSeederAsync(services, cancellationToken);

        (Guid Id, string Username, string PasswordHash, DateTimeOffset CreatedAt)[] after = Fingerprint(await AllUsersAsync(services, cancellationToken));

        // Same rows, same ids, same hashes, same instants. Not "the same count" — a re-seed that
        // rewrote a row would keep the count and still be a change.
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Concurrent_starts_serialize_on_the_advisory_lock_and_still_seed_once()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.CreateDatabaseAsync(nameof(Concurrent_starts_serialize_on_the_advisory_lock_and_still_seed_once), cancellationToken);

        await using ServiceProvider migrator = TestHost.Build(connectionString, TestHost.SeedingOff);
        await TestHost.MigrateAsync(migrator, cancellationToken);

        // Two independent hosts against one database, started together — two api replicas, or a
        // restart racing a start. pg_advisory_xact_lock is what makes the second one a no-op.
        SeedSettings seeding = TestHost.SeedingOn();

        await using ServiceProvider first = TestHost.Build(connectionString, seeding);
        await using ServiceProvider second = TestHost.Build(connectionString, seeding);

        await Task.WhenAll(
            TestHost.StartSeederAsync(first, cancellationToken),
            TestHost.StartSeederAsync(second, cancellationToken));

        Assert.Equal(4, (await AllUsersAsync(first, cancellationToken)).Count);
    }

    [Fact]
    public async Task Seeding_off_writes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using ServiceProvider services = await MigratedHostAsync(
            nameof(Seeding_off_writes_nothing), TestHost.SeedingOff, cancellationToken);

        await TestHost.StartSeederAsync(services, cancellationToken);

        Assert.Empty(await AllUsersAsync(services, cancellationToken));
    }

    private async Task<ServiceProvider> MigratedHostAsync(
        string name,
        SeedSettings seed,
        CancellationToken cancellationToken)
    {
        string connectionString = await postgres.CreateDatabaseAsync(name, cancellationToken);

        ServiceProvider services = TestHost.Build(connectionString, seed);

        await TestHost.MigrateAsync(services, cancellationToken);

        return services;
    }

    private static async Task<List<User>> AllUsersAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Users.AsNoTracking()
            .OrderBy(user => user.Username)
            .ToListAsync(cancellationToken);
    }

    private static (Guid Id, string Username, string PasswordHash, DateTimeOffset CreatedAt)[] Fingerprint(
        IEnumerable<User> users) =>
        [.. users.Select(user => (user.Id, user.Username, user.PasswordHash, user.CreatedAt))];

    private static User Single(IEnumerable<User> users, string username) =>
        users.Single(user => user.Username == username);
}
