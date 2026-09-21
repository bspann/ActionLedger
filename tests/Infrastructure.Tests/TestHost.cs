using ActionLedger.Infrastructure.Persistence;
using ActionLedger.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// Builds the Infrastructure ring exactly as the composition root does — the same registration
/// methods, the same <c>AppDbContext</c>, the same seeder — over a throwaway database.
/// </summary>
/// <remarks>
/// Nothing here substitutes a fake for a persistence concern. If a test passes against this host
/// and fails against the api, the difference is the Api ring, not the wiring.
/// </remarks>
internal static class TestHost
{
    /// <summary>
    /// A password invented for one call. Never a committed literal — NFR5 keeps credentials out of
    /// the repository, and a test constant is still a credential in a public one. A test carries
    /// the value it generated, so no assertion here can pass by agreeing with something on disk.
    /// </summary>
    public static string NewPassword() => $"test-{Guid.CreateVersion7():N}";

    /// <summary>Seeding on, with a fresh password. Read it back off the returned settings.</summary>
    public static SeedSettings SeedingOn() => new(Enabled: true, DefaultPassword: NewPassword());

    /// <summary>
    /// Seeding off, as <c>cd.yml</c> and <c>Api.Tests</c> run it. The password is blank, which is
    /// legal only because nothing reads it while seeding is off — and a test proves that.
    /// </summary>
    public static SeedSettings SeedingOff => new(Enabled: false, DefaultPassword: string.Empty);

    public static ServiceProvider Build(
        string connectionString,
        SeedSettings? seed = null,
        Action<IServiceCollection>? configure = null)
    {
        ServiceCollection services = new();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddActionLedgerPersistence(_ => connectionString);
        services.AddActionLedgerSeeding(_ => seed ?? SeedingOn());

        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Applies the migrations. AD-17 keeps the api from ever doing this; the bundle does it in
    /// compose and in CD, and a test does it here — which is also what proves the migration the
    /// bundle carries actually applies to an empty database.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(cancellationToken);
    }

    /// <summary>Runs the seeder the way the host runs it: one <c>StartAsync</c>.</summary>
    public static Task StartSeederAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetServices<IHostedService>().OfType<DemoDataSeeder>().Single().StartAsync(cancellationToken);
}
