using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActionLedger.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>tracked_actions</c> table (AD-3, AD-4, AD-20). The snake_case names, <c>date</c> for the
/// due date, <c>timestamptz</c> for the instant, the string <c>status</c>, and
/// <c>ValueGeneratedNever</c> on the key come from <see cref="ModelConventions"/>; what is here is
/// specific to this root.
/// </summary>
/// <remarks>
/// <para>
/// Both foreign keys are shadow relationships with no navigation — AD-3 has roots reference each
/// other by id — and both restrict deletes: a Tracked Action is an audited record, and neither its
/// proposal nor its owner may disappear from under it.
/// </para>
/// <para>
/// The unique index on <c>proposed_action_id</c> is the AD-20 backstop for two officers deciding
/// one proposal at once. The proposal's <c>xmin</c> token turns the loser's commit into a 409
/// first; this index refuses a second Tracked Action for any writer that got past it.
/// </para>
/// </remarks>
internal sealed class TrackedActionConfiguration : IEntityTypeConfiguration<TrackedAction>
{
    /// <summary>The name of the AD-20 unique index on <c>proposed_action_id</c>, so a test can name it.</summary>
    public const string ProposedActionIndexName = "ix_tracked_actions_proposed_action_id";

    /// <summary>The name of the index behind the Action List's due-date and status filters.</summary>
    public const string DueDateAndStatusIndexName = "ix_tracked_actions_due_date_status";

    /// <summary>The name of the index behind the Action List's owner filter.</summary>
    public const string OwnerIndexName = "ix_tracked_actions_owner_user_id";

    /// <summary>
    /// The shadow property carrying PostgreSQL's own row version. Named for the system column it
    /// maps to, so <see cref="ModelConventions"/>' snake_case sweep leaves it alone.
    /// </summary>
    public const string ConcurrencyTokenProperty = "xmin";

    /// <summary>The widest <c>ActionStatus</c> name, with headroom.</summary>
    private const int StatusMaxLength = 32;

    public void Configure(EntityTypeBuilder<TrackedAction> builder)
    {
        builder.HasKey(action => action.Id);

        builder.Property(action => action.ProposedActionId)
            .IsRequired();

        builder.Property(action => action.Description)
            .IsRequired()
            .HasMaxLength(TrackedAction.DescriptionMaxLength);

        // Nullable, and that null is Unassigned rather than a gap.
        builder.Property(action => action.OwnerUserId);

        builder.Property(action => action.DueDate);

        builder.Property(action => action.Status)
            .IsRequired()
            .HasMaxLength(StatusMaxLength);

        builder.Property(action => action.CreatedAt)
            .IsRequired();

        // AD-20 — TrackedAction is one of the four roots with a state machine. NpgsqlConcurrency-
        // TokenConvention binds this shape to the `xmin` system column, so no column is added.
        builder.Property<uint>(ConcurrencyTokenProperty)
            .IsRowVersion();

        // Domain events are raised in memory and drained inside the commit; they are not a column.
        builder.Ignore(action => action.DomainEvents);

        builder.HasOne<ProposedAction>()
            .WithMany()
            .HasForeignKey(action => action.ProposedActionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(action => action.OwnerUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(action => action.ProposedActionId)
            .IsUnique()
            .HasDatabaseName(ProposedActionIndexName);

        builder.HasIndex(action => new { action.DueDate, action.Status })
            .HasDatabaseName(DueDateAndStatusIndexName);

        builder.HasIndex(action => action.OwnerUserId)
            .HasDatabaseName(OwnerIndexName);
    }
}
