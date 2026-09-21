using System.ComponentModel.DataAnnotations;

namespace ActionLedger.Api.Configuration;

/// <summary>
/// The <c>Database</c> section (AD-16). Validated now so a misconfigured host fails at startup
/// rather than at the first query; the <c>DbContext</c> that consumes it arrives with Story 1.3.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>The PostgreSQL connection string. A secret: environment or user secrets only.</summary>
    [Required(ErrorMessage = "Database:ConnectionString is required.")]
    public string ConnectionString { get; init; } = string.Empty;
}
