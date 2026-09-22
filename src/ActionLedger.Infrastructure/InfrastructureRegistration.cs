using ActionLedger.Application.Abstractions;
using ActionLedger.Infrastructure.Auth;
using ActionLedger.Infrastructure.Persistence;
using ActionLedger.Infrastructure.Seed;
using ActionLedger.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActionLedger.Infrastructure;

/// <summary>
/// What the composition root wires up. Every EF Core type stays behind this file: the Api adds
/// persistence and seeding by calling these two methods and never names a <c>DbContext</c>,
/// a provider, or a repository implementation.
/// </summary>
/// <remarks>
/// Both methods take accessors rather than values. The Api's <c>DatabaseOptions</c> and
/// <c>SeedOptions</c> are the AD-16 <c>ValidateOnStart</c> guards, and they are only resolvable
/// once the provider is built — so the accessor defers the read to first use and there is still
/// exactly one definition of each configuration key.
/// </remarks>
public static class InfrastructureRegistration
{
    /// <summary>
    /// Registers <see cref="AppDbContext"/>, the repositories, the read seam, the unit of work,
    /// the readiness probe, the password hasher and verifier, and the system clock.
    /// </summary>
    /// <remarks>
    /// Nothing here needs an <c>HttpContext</c>. AD-1 Rule 4 keeps the claims-backed
    /// <c>ICurrentUser</c> and the token issuer in the Api ring, where the <c>Jwt</c> options live.
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="connectionString">
    /// Reads <c>Database:ConnectionString</c> from the validated options. It is read on first
    /// use rather than at registration, which is what lets <c>--export-openapi</c> build the app
    /// and read the document without a database or a connection string anywhere in sight.
    /// </param>
    public static IServiceCollection AddActionLedgerPersistence(
        this IServiceCollection services,
        Func<IServiceProvider, string?> connectionString)
    {
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseNpgsql(connectionString(provider), Resilience));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IMeetingRepository, MeetingRepository>();
        services.AddScoped<IReadDb, ReadDb>();
        services.AddScoped<SeedRepository>();
        services.AddScoped<DatabaseReadiness>();

        services.AddSingleton<PasswordService>();
        services.AddSingleton<IPasswordVerifier, PasswordVerifier>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }

    /// <summary>
    /// Registers the AD-21 seeder as a hosted service. Register it before the outbox dispatcher,
    /// so seeding finishes before the first poll.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="settings">Reads the <c>Seed</c> section from the validated options.</param>
    public static IServiceCollection AddActionLedgerSeeding(
        this IServiceCollection services,
        Func<IServiceProvider, SeedSettings> settings)
    {
        services.AddSingleton(settings);
        services.AddHostedService<DemoDataSeeder>();

        return services;
    }

    /// <summary>
    /// A transient connection failure on a cold compose start is not a reason to fail the host.
    /// The seeder runs its whole unit of work inside the execution strategy, so a retry replays
    /// the lock, the reads, and the writes together rather than half of them.
    /// </summary>
    private static void Resilience(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.EnableRetryOnFailure();
}
