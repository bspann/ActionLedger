using ActionLedger.Domain.Actions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActionLedger.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>action_revisions</c> table (AD-7, ADR-004). The snake_case names, the string
/// <c>target_type</c> and <c>kind</c>, the <c>timestamptz</c> instant, and
/// <c>ValueGeneratedNever</c> on the key come from <see cref="ModelConventions"/>.
/// </summary>
/// <remarks>
/// <para>
/// There is no foreign key to <c>proposed_actions</c> and no navigation from any aggregate, by
/// design: <c>(target_type, target_id)</c> is a polymorphic reference that will name Tracked
/// Actions as well as proposals once Story 3.2 lands, and a per-target foreign key would have to
/// be nullable and be added again per target kind.
/// </para>
/// <para>
/// AD-20 gives this type no concurrency token. It is append-only — nothing updates a revision, so
/// there is no write for a token to arbitrate.
/// </para>
/// </remarks>
internal sealed class ActionRevisionConfiguration : IEntityTypeConfiguration<ActionRevision>
{
    /// <summary>
    /// The name of the non-unique index the spine's Indexes row lists,
    /// <c>action_revision(target_type, target_id, sequence)</c> — the Audit Trail's read order.
    /// </summary>
    public const string TargetIndexName = "ix_action_revisions_target_type_target_id_sequence";

    /// <summary>The widest <c>RevisionTargetType</c> and <c>RevisionKind</c> name, with headroom.</summary>
    private const int DiscriminatorMaxLength = 50;

    public void Configure(EntityTypeBuilder<ActionRevision> builder)
    {
        builder.HasKey(revision => revision.Id);

        builder.Property(revision => revision.TargetType)
            .IsRequired()
            .HasMaxLength(DiscriminatorMaxLength);

        builder.Property(revision => revision.TargetId)
            .IsRequired();

        builder.Property(revision => revision.Sequence)
            .IsRequired();

        builder.Property(revision => revision.Kind)
            .IsRequired()
            .HasMaxLength(DiscriminatorMaxLength);

        builder.Property(revision => revision.Field)
            .HasMaxLength(ActionRevision.FieldMaxLength);

        // Unbounded text on purpose. The new value of an AiProposal revision is the whole proposal
        // as JSON, and a length cap here would be a second, silent copy of the schema's bounds.
        builder.Property(revision => revision.OldValue);

        builder.Property(revision => revision.NewValue);

        // Nullable, and that null is the record rather than a gap: the AI is not a User (AD-7).
        builder.Property(revision => revision.ActorUserId);

        builder.Property(revision => revision.OccurredAt)
            .IsRequired();

        // Domain events are raised in memory and drained inside the commit; they are not a column.
        builder.Ignore(revision => revision.DomainEvents);

        // The Audit Trail is one ordered read over this index (AD-7, spine Indexes row). Not
        // unique: one aggregate call writes several revisions against one target, and a later
        // story's decision writes more.
        //
        // Note what that leaves unguarded. AD-7 calls Sequence "strictly increasing per
        // (TargetType, TargetId)", but nothing in the database enforces it. Story 2.5 is safe by
        // construction — an AiProposal revision targets a proposal that was minted in the same
        // breath, so its sequence is 1 and there is no prior row to collide with. A decision's
        // sequences are fixed by construction too: ProposedAction.Decide numbers its
        // ReviewDecision FirstSequence + 1 with no read, because a Pending proposal has exactly its
        // AiProposal revision. So two officers deciding one proposal concurrently would both write
        // sequence 2. What stops the duplicate is the AD-20 `xmin` token on ProposedAction, which
        // makes the second decision's commit a 409 before its revisions land — not this index. If a
        // writer ever appends a revision without touching a token-carrying row, this index has to
        // become unique on those three columns.
        builder.HasIndex(revision => new { revision.TargetType, revision.TargetId, revision.Sequence })
            .HasDatabaseName(TargetIndexName);
    }
}
