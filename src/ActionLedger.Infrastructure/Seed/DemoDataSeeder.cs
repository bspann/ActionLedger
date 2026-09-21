using System.Security.Cryptography;
using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Users;
using ActionLedger.Infrastructure.Auth;
using ActionLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActionLedger.Infrastructure.Seed;

/// <summary>
/// AD-21 — the demo seeder. A hosted service inside the api, gated on <c>Seed:Enabled</c>,
/// serialized by a PostgreSQL advisory lock, and idempotent: a second start changes nothing.
/// </summary>
/// <remarks>
/// <para>
/// This story seeds the four users and nothing else. Story 6.1 extends it with the fixture-driven
/// Meetings, runs, proposals, decisions, and the delivered and dead outbox rows — which is why
/// the actor and the clock are already what AD-21 requires: writes go through aggregate methods
/// with <see cref="SeedCurrentUser"/> and a <see cref="FixedClock"/>, so seeded rows come out of
/// the same code paths as live ones. There are no raw inserts here, and there will not be.
/// </para>
/// <para>
/// It is registered before the outbox dispatcher so it finishes before the first poll (AD-21),
/// and it never migrates: the schema is already there, applied by the bundle (AD-17).
/// </para>
/// </remarks>
public sealed class DemoDataSeeder(
    IServiceScopeFactory scopeFactory,
    SeedSettings settings,
    ILogger<DemoDataSeeder> logger) : IHostedService
{
    /// <summary>The <c>Seed</c> User's sign-in name. Flagged <c>isSystem</c>; no usable password.</summary>
    public const string SystemUsername = "seed";

    /// <summary>The <c>Seed</c> User's display name, as it appears on seeded data.</summary>
    public const string SystemDisplayName = "Seed";

    /// <summary>
    /// The three people the demo signs in as. The username is what the login screen takes; the
    /// password is <see cref="SeedSettings.DefaultPassword"/>, which is configuration, not code.
    /// </summary>
    public static readonly IReadOnlyList<DemoUser> DemoUsers =
    [
        new("dana", "Dana Whitfield", Role.ActionOfficer),
        new("priya", "Priya Ramaswamy", Role.ActionOfficer),
        new("marcus", "Marcus Bell", Role.Lead),
    ];

    /// <summary>One seeded human User.</summary>
    /// <param name="Username">The sign-in name.</param>
    /// <param name="DisplayName">The name shown in the roster and the audit trail.</param>
    /// <param name="Role">The role the token carries.</param>
    public sealed record DemoUser(string Username, string DisplayName, Role Role);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            logger.LogInformation("Seeding is off (Seed:Enabled=false). No demo data was written.");

            return;
        }

        if (string.IsNullOrWhiteSpace(settings.DefaultPassword))
        {
            // The Api's ValidateOnStart already stops a host here, before any hosted service runs
            // (AD-16). This is the same refusal one ring lower, so the invariant belongs to the
            // seeder rather than to whoever happens to compose it: there is no default credential
            // to fall back to, and inventing one would be the bug.
            throw new InvalidOperationException(
                "Seed:DefaultPassword is required when Seed:Enabled is true. "
                + "Supply it from the environment or user secrets; there is no default.");
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The retrying execution strategy owns the transaction boundary, so the whole unit —
        // lock, reads, writes, commit — is what gets retried, never half of it.
        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(
            scope.ServiceProvider,
            async (_, services, token) =>
            {
                await SeedAsync(services, context, token);

                return true;
            },
            verifySucceeded: null,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedAsync(
        IServiceProvider services,
        AppDbContext context,
        CancellationToken cancellationToken)
    {
        IUserRepository users = services.GetRequiredService<IUserRepository>();
        IUnitOfWork unitOfWork = services.GetRequiredService<IUnitOfWork>();
        SeedRepository seedRepository = services.GetRequiredService<SeedRepository>();
        PasswordService passwords = services.GetRequiredService<PasswordService>();
        IClock clock = FixedClock.AtSeedInstant();

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        // Everything below runs under the advisory lock, so a second api starting at the same
        // moment waits here and then finds every row already present.
        await seedRepository.AcquireLockAsync(cancellationToken);

        User seedUser = await EnsureSystemUserAsync(users, passwords, clock, cancellationToken);

        int created = 0;

        foreach (DemoUser demo in DemoUsers)
        {
            if (await users.FindByUsernameAsync(demo.Username, cancellationToken) is not null)
            {
                continue;
            }

            users.Add(User.Register(
                demo.Username,
                demo.DisplayName,
                // Salted per password, so the same demo credential still hashes differently per
                // row. Idempotence comes from the existence check above and from the unique
                // index — never from the hash being stable.
                passwords.Hash(settings.DefaultPassword),
                demo.Role,
                clock.UtcNow));

            created++;
        }

        await unitOfWork.CommitAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // AD-21's attribution identity, built from the persisted row so it always points at a
        // User that exists. Nothing this story writes carries an actor column — the User
        // aggregate has none — but Story 6.1's Meetings, runs, and decisions all take it.
        SeedCurrentUser actor = new(seedUser);

        logger.LogInformation(
            "Seeding complete. {CreatedCount} user(s) created; seeded data is attributed to {ActorDisplayName} ({ActorId}).",
            created,
            actor.DisplayName,
            actor.UserId);
    }

    private static async Task<User> EnsureSystemUserAsync(
        IUserRepository users,
        PasswordService passwords,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (await users.FindByUsernameAsync(SystemUsername, cancellationToken) is { } existing)
        {
            return existing;
        }

        // The column is not nullable, so the system User gets a hash of a value generated here,
        // never returned and never written anywhere else. No password signs this account in.
        string unusable = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        User seedUser = User.RegisterSystem(
            SystemUsername,
            SystemDisplayName,
            passwords.Hash(unusable),
            clock.UtcNow);

        users.Add(seedUser);

        return seedUser;
    }
}
