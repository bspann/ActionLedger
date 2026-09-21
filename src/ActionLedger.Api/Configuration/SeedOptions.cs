namespace ActionLedger.Api.Configuration;

/// <summary>
/// The <c>Seed</c> section (AD-16, AD-21). Enabled by default so compose comes up with demo data;
/// <c>cd.yml</c> sets it false. <c>DemoDataSeeder</c> in Infrastructure is what reads these.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>The configuration key <see cref="DefaultPassword"/> binds to, as a message names it.</summary>
    public const string DefaultPasswordKey = $"{SectionName}:DefaultPassword";

    /// <summary>Whether the seeder runs at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// The password the seeded human users are given, supplied from the environment or user
    /// secrets — never from a file in this repository.
    /// </summary>
    /// <remarks>
    /// There is deliberately no default. NFR5 keeps credentials out of the repository, and a
    /// built-in fallback would be worse than no password at all: every clone would seed the same
    /// guessable one and nobody would have to notice. <see cref="SeedOptionsValidator"/> fails
    /// the host at startup when seeding is on and this is absent, naming the key. The <c>Seed</c>
    /// system User never gets a usable password, whatever this is set to.
    /// </remarks>
    public string DefaultPassword { get; init; } = string.Empty;
}
