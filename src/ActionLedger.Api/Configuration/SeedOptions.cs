namespace ActionLedger.Api.Configuration;

/// <summary>
/// The <c>Seed</c> section (AD-16, AD-18). True by default so compose comes up with demo data;
/// <c>cd.yml</c> sets it false. The seeder itself is a hosted service that arrives with Story 1.3.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; init; } = true;
}
