using ActionLedger.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActionLedger.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>users</c> table. Column names, the snake_case table name, the string <c>role</c>, the
/// <c>timestamptz</c> instant, and <c>ValueGeneratedNever</c> on the key all come from
/// <see cref="ModelConventions"/>; what is here is what is specific to this aggregate.
/// </summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>The name of the AD-20 unique index, spelled out so a test can name it.</summary>
    public const string UsernameIndexName = "ix_users_username";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(user => user.Id);

        builder.Property(user => user.Username)
            .IsRequired()
            .HasMaxLength(User.UsernameMaxLength);

        builder.Property(user => user.DisplayName)
            .IsRequired()
            .HasMaxLength(User.DisplayNameMaxLength);

        builder.Property(user => user.PasswordHash)
            .IsRequired();

        builder.Property(user => user.Role)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(user => user.IsSystem)
            .IsRequired();

        builder.Property(user => user.CreatedAt)
            .IsRequired();

        // AD-20 — `user(username)` is on the unique-index list. It is also what makes the seeder
        // idempotent under a concurrent second start: the advisory lock serializes the normal
        // case, and this index is the backstop that turns a lost race into a 409 rather than a
        // duplicate row.
        builder.HasIndex(user => user.Username)
            .IsUnique()
            .HasDatabaseName(UsernameIndexName);

        // Domain events are raised in memory and drained inside the commit; they are not a column.
        builder.Ignore(user => user.DomainEvents);
    }
}
