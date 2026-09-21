using System.ComponentModel.DataAnnotations;

namespace ActionLedger.Api.Configuration;

/// <summary>
/// The <c>Jwt</c> section (AD-12, AD-16). This story accepts and validates bearer tokens;
/// Story 1.4 issues them against the same key and issuer.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>AD-12: an 8-hour lifetime. Story 1.4 stamps it; nothing configures it.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);

    /// <summary>
    /// The HS256 signing key. A secret — it comes from the environment or user secrets, never
    /// from a committed file. HS256 needs at least 256 bits of key material.
    /// </summary>
    [Required(ErrorMessage = "Jwt:Key is required.")]
    [MinLength(32, ErrorMessage = "Jwt:Key must be at least 32 characters — HS256 signs with a 256-bit key.")]
    public string Key { get; init; } = string.Empty;

    [Required(ErrorMessage = "Jwt:Issuer is required.")]
    public string Issuer { get; init; } = string.Empty;
}
