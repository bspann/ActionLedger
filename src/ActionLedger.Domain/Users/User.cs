using ActionLedger.Domain.Common;

namespace ActionLedger.Domain.Users;

/// <summary>
/// Someone who signs in and whose name is stamped on every write they make (AD-12). The only
/// aggregate this story brings; Meetings, runs, proposals, and actions arrive with their epics.
/// </summary>
/// <remarks>
/// <para>
/// There is no public constructor and no public setter. A User comes into existence through
/// <see cref="Register"/> or <see cref="RegisterSystem"/>, and the seeder uses exactly those two
/// (AD-21) — it never writes a row directly, so a rule added here applies to seeded users too.
/// </para>
/// <para>
/// The password is a hash produced by <c>PasswordHasher&lt;User&gt;</c> in Infrastructure. Domain
/// never hashes and never compares; it only refuses to hold an empty one.
/// </para>
/// </remarks>
public sealed class User : AggregateRoot
{
    /// <summary>The longest username the unique index has to carry.</summary>
    public const int UsernameMaxLength = 64;

    /// <summary>The longest display name shown in the roster, the toolbar, and the audit trail.</summary>
    public const int DisplayNameMaxLength = 128;

    private User(
        string username,
        string displayName,
        string passwordHash,
        Role role,
        bool isSystem,
        DateTimeOffset createdAt)
    {
        Username = username;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        Role = role;
        IsSystem = isSystem;
        CreatedAt = createdAt;
    }

    /// <summary>Rehydration constructor for EF Core.</summary>
    private User()
    {
        Username = string.Empty;
        DisplayName = string.Empty;
        PasswordHash = string.Empty;
    }

    /// <summary>
    /// The sign-in name, lower-cased and trimmed so "Dana" and "dana" cannot both exist. The
    /// <c>users(username)</c> unique index is what enforces that across concurrent writers (AD-20).
    /// </summary>
    public string Username { get; private set; }

    /// <summary>The name shown in the roster, the toolbar, and every audit entry.</summary>
    public string DisplayName { get; private set; }

    /// <summary>A <c>PasswordHasher&lt;User&gt;</c> hash. Never a password, never logged.</summary>
    public string PasswordHash { get; private set; }

    /// <summary>The role the token carries and authorization reads (AD-12).</summary>
    public Role Role { get; private set; }

    /// <summary>
    /// True for the <c>Seed</c> User that owns seeded data. Story 1.4 excludes system users from
    /// the roster the web app consumes and refuses to sign one in.
    /// </summary>
    public bool IsSystem { get; private set; }

    /// <summary>When the User was created, from <c>IClock</c> — never <c>DateTimeOffset.UtcNow</c> (AD-15).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates a human User who can sign in.</summary>
    /// <param name="username">The sign-in name; normalized to lower case and trimmed.</param>
    /// <param name="displayName">The name shown wherever this User is attributed.</param>
    /// <param name="passwordHash">A hash from <c>PasswordHasher&lt;User&gt;</c>, never a password.</param>
    /// <param name="role">The role the User signs in as.</param>
    /// <param name="createdAt">The creation instant, from <c>IClock</c>.</param>
    /// <exception cref="DomainRuleException">A required field is blank or too long.</exception>
    public static User Register(
        string username,
        string displayName,
        string passwordHash,
        Role role,
        DateTimeOffset createdAt) =>
        new(
            NormalizeUsername(username),
            RequireText(displayName, nameof(displayName), DisplayNameMaxLength),
            RequireHash(passwordHash),
            role,
            isSystem: false,
            createdAt.ToUniversalTime());

    /// <summary>
    /// Creates the system User that owns seeded data (AD-21). It is flagged
    /// <see cref="IsSystem"/> and still carries a hash, because the column is not nullable — the
    /// seeder hashes a value nobody holds, so there is no password that signs this account in.
    /// </summary>
    /// <param name="username">The sign-in name; normalized to lower case and trimmed.</param>
    /// <param name="displayName">The name shown wherever seeded data is attributed.</param>
    /// <param name="unusablePasswordHash">A hash of a value the caller discards.</param>
    /// <param name="createdAt">The creation instant, from <c>IClock</c>.</param>
    /// <exception cref="DomainRuleException">A required field is blank or too long.</exception>
    public static User RegisterSystem(
        string username,
        string displayName,
        string unusablePasswordHash,
        DateTimeOffset createdAt) =>
        new(
            NormalizeUsername(username),
            RequireText(displayName, nameof(displayName), DisplayNameMaxLength),
            RequireHash(unusablePasswordHash),
            // Lead, so seeded data can include the one Lead-only transition (Cancelled) without
            // the seeder needing a second identity.
            Role.Lead,
            isSystem: true,
            createdAt.ToUniversalTime());

    private static string NormalizeUsername(string username)
    {
        string normalized = RequireText(username, nameof(username), UsernameMaxLength).ToLowerInvariant();

        if (normalized.Any(char.IsWhiteSpace))
        {
            throw new DomainRuleException("A username cannot contain whitespace.");
        }

        return normalized;
    }

    private static string RequireHash(string passwordHash) =>
        string.IsNullOrWhiteSpace(passwordHash)
            ? throw new DomainRuleException("A User must have a password hash.")
            : passwordHash;

    private static string RequireText(string value, string field, int maxLength)
    {
        string trimmed = (value ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainRuleException($"A User's {field} is required.");
        }

        return trimmed.Length > maxLength
            ? throw new DomainRuleException($"A User's {field} cannot exceed {maxLength} characters.")
            : trimmed;
    }
}
