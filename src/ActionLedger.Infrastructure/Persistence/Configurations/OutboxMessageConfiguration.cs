using ActionLedger.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActionLedger.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>outbox_messages</c> table (AD-8, ADR-005). Snake_case names, the string <c>state</c>,
/// <c>timestamptz</c> for the instants and <c>ValueGeneratedNever</c> on the key come from
/// <see cref="ModelConventions"/>; what is here is specific to this root.
/// </summary>
/// <remarks>
/// <para>
/// The subscription is a shadow relationship with no navigation, and it restricts deletes: a
/// delivery record outlives nothing it points at. There is no foreign key to the Tracked Action —
/// the ERD has no such column, and the payload already names it.
/// </para>
/// <para>
/// <c>(state, next_attempt_at)</c> is the dispatcher's claim query in Epic 5: Pending rows that are
/// due, oldest first.
/// </para>
/// </remarks>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <summary>The name of the dispatcher's claim index, so a test can name it.</summary>
    public const string StateAndNextAttemptIndexName = "ix_outbox_messages_state_next_attempt_at";

    /// <summary>
    /// The shadow property carrying PostgreSQL's own row version. Named for the system column it
    /// maps to, so <see cref="ModelConventions"/>' snake_case sweep leaves it alone.
    /// </summary>
    public const string ConcurrencyTokenProperty = "xmin";

    /// <summary>The widest <c>OutboxState</c> name, with headroom.</summary>
    private const int StateMaxLength = 32;

    /// <summary>The longest failure message the <c>last_error</c> column holds.</summary>
    private const int LastErrorMaxLength = 2_000;

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.HasKey(message => message.Id);

        builder.Property(message => message.SubscriptionId)
            .IsRequired();

        builder.Property(message => message.EventType)
            .IsRequired()
            .HasMaxLength(WebhookSubscription.EventTypeMaxLength);

        builder.Property(message => message.EventId)
            .IsRequired();

        builder.Property(message => message.Payload)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(message => message.State)
            .IsRequired()
            .HasMaxLength(StateMaxLength);

        builder.Property(message => message.AttemptCount)
            .IsRequired();

        builder.Property(message => message.NextAttemptAt)
            .IsRequired();

        builder.Property(message => message.LastStatusCode);

        builder.Property(message => message.LastError)
            .HasMaxLength(LastErrorMaxLength);

        builder.Property(message => message.CreatedAt)
            .IsRequired();

        // AD-20 — OutboxMessage is one of the four roots with a state machine, and two dispatchers
        // may race one row. NpgsqlConcurrencyTokenConvention binds this shape to the `xmin` system
        // column, so no column is added.
        builder.Property<uint>(ConcurrencyTokenProperty)
            .IsRowVersion();

        // Domain events are raised in memory and drained inside the commit; they are not a column.
        builder.Ignore(message => message.DomainEvents);

        builder.HasOne<WebhookSubscription>()
            .WithMany()
            .HasForeignKey(message => message.SubscriptionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(message => new { message.State, message.NextAttemptAt })
            .HasDatabaseName(StateAndNextAttemptIndexName);
    }
}
