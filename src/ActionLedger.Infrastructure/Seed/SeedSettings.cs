namespace ActionLedger.Infrastructure.Seed;

/// <summary>
/// What the seeder needs from configuration, handed to this ring by the composition root.
/// </summary>
/// <remarks>
/// The keys themselves (<c>Seed:Enabled</c>, <c>Seed:DefaultPassword</c>) are bound and validated
/// by <c>SeedOptions</c> in the Api, which is where AD-16's fail-fast lives. This record is the
/// validated values arriving here — Infrastructure reads no configuration of its own, so there is
/// one definition of each key and no second place a default could hide.
/// </remarks>
/// <param name="Enabled">AD-21 — seeding runs only when this is true. Default true in compose, false in <c>cd.yml</c>.</param>
/// <param name="DefaultPassword">
/// The password the three seeded human users are given. It comes from the environment or user
/// secrets and has no default anywhere — nothing in this repository holds a credential value
/// (NFR5). Blank while <paramref name="Enabled"/> is true is a configuration error, not a cue to
/// invent one. The <c>Seed</c> User never gets it.
/// </param>
public sealed record SeedSettings(bool Enabled, string DefaultPassword);
