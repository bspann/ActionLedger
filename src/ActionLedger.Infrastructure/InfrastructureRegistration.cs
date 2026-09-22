using ActionLedger.Application.Abstractions;
using ActionLedger.Infrastructure.Ai;
using ActionLedger.Infrastructure.Ai.Providers;
using ActionLedger.Infrastructure.Auth;
using ActionLedger.Infrastructure.Persistence;
using ActionLedger.Infrastructure.Seed;
using ActionLedger.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActionLedger.Infrastructure;

/// <summary>
/// What the composition root wires up. Every EF Core type and every AI SDK type stays behind this
/// file: the Api adds persistence, seeding and the AI seam by calling these three methods and never
/// names a <c>DbContext</c>, an <c>IChatClient</c>, or a repository implementation.
/// </summary>
/// <remarks>
/// All three methods take accessors rather than values. The Api's <c>DatabaseOptions</c>,
/// <c>SeedOptions</c> and <c>AiOptions</c> are the AD-16 <c>ValidateOnStart</c> guards, and they are only resolvable
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
        services.AddScoped<IExtractionRunRepository, ExtractionRunRepository>();
        services.AddScoped<IActionRevisionRepository, ActionRevisionRepository>();
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
    /// AD-11 — the AI seam: the prompt and fixture catalogs, the keyed provider factories, the one
    /// chat client, the one extractor, the provider-info port, and AD-16's startup check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Register this before <see cref="AddActionLedgerSeeding"/>. Hosted services start in
    /// registration order, so putting <see cref="AiStartupCheck"/> first means a missing prompt
    /// version or an unregistered provider fails the host before the seeder writes a row — the same
    /// reason the seeder itself is registered before the outbox dispatcher.
    /// </para>
    /// <para>
    /// FR-7 — adding a provider is one factory class in <c>Ai/Providers</c> plus one
    /// <c>AddKeyedSingleton</c> line here. This story registers the Fake and nothing else; Story
    /// 2.7 adds LocalOpenAI and AzureOpenAI and touches no other file in <c>src/</c>.
    /// </para>
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="settings">
    /// Reads the <c>Ai</c> section from the validated options. An accessor rather than a value, for
    /// <see cref="AddActionLedgerPersistence"/>'s reason: the Api's <c>ValidateOnStart</c> options
    /// are only resolvable once the provider is built.
    /// </param>
    public static IServiceCollection AddActionLedgerAi(
        this IServiceCollection services,
        Func<IServiceProvider, AiSettings> settings)
    {
        services.AddSingleton(settings);

        // Both catalogs read embedded resources once, so they are singletons and there is no path,
        // no copy step and no per-request I/O behind either (AD-6, AD-21).
        services.AddSingleton<FixtureCatalog>();
        services.AddSingleton<IPromptCatalog, PromptCatalog>();

        // The one line Story 2.7 adds a sibling to. The key is the Ai:Provider value itself.
        services.AddKeyedSingleton<IChatClientFactory, FakeChatClientFactory>(FakeChatClientFactory.ProviderName);

        // The active factory, resolved by name through the one lookup AiStartupCheck also uses, so
        // a misconfiguration reads the same wherever it surfaces.
        services.AddSingleton(ActiveFactory);
        services.AddSingleton<IChatClient>(provider => provider.GetRequiredService<IChatClientFactory>().Create());

        // AD-15 keeps IClock for domain timestamps; the extractor needs a timer for the per-call
        // budget, which is TimeProvider's job. TryAdd, because the host may already have one.
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IActionExtractor, ChatClientActionExtractor>();
        services.AddSingleton<IAiProviderInfo, AiProviderInfo>();

        // AD-16 — the Application ring reads Ai:LowConfidenceThreshold only through this port.
        // A singleton over the settings record: nothing here is per-request.
        services.AddSingleton<IExtractionSettings, ExtractionSettings>();

        services.AddHostedService<AiStartupCheck>();

        return services;
    }

    /// <summary>
    /// The <see cref="IChatClientFactory"/> keyed by the configured <c>Ai:Provider</c>.
    /// </summary>
    private static IChatClientFactory ActiveFactory(IServiceProvider provider) =>
        ChatClientFactories.Resolve(provider, provider.GetRequiredService<AiSettings>().Provider);

    /// <summary>
    /// A transient connection failure on a cold compose start is not a reason to fail the host.
    /// The seeder runs its whole unit of work inside the execution strategy, so a retry replays
    /// the lock, the reads, and the writes together rather than half of them.
    /// </summary>
    private static void Resilience(Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.EnableRetryOnFailure();
}
