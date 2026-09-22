using ActionLedger.Domain.Extraction;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActionLedger.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>proposed_actions</c> table (AD-4, AD-20). The snake_case names, <c>date</c> for the due
/// date, the string <c>review_state</c>, and <c>ValueGeneratedNever</c> on the key come from
/// <see cref="ModelConventions"/>; what is here is specific to this entity.
/// </summary>
/// <remarks>
/// <para>
/// The table name is declared because <c>ProposedAction</c> has no <c>DbSet</c> — it has no
/// lifetime of its own (AD-3), and EF would otherwise name the table after the entity type and
/// produce <c>proposed_action</c>. The Consistency Conventions make tables plural, so the name is
/// spelled here in PascalCase and <see cref="ModelConventions"/> snake-cases it, exactly as
/// <see cref="MeetingConfiguration"/> does for the owned notes.
/// </para>
/// <para>
/// AD-20 puts a concurrency token on this type and not on its run: a proposal is a row two Action
/// Officers can race to decide, which is the whole reason Story 3.2 needs a 409 to give one of
/// them.
/// </para>
/// </remarks>
internal sealed class ProposedActionConfiguration : IEntityTypeConfiguration<ProposedAction>
{
    /// <summary>The name of the AD-20 unique index on <c>(extraction_run_id, ordinal)</c>.</summary>
    public const string RunAndOrdinalIndexName = "ix_proposed_actions_extraction_run_id_ordinal";

    /// <summary>
    /// The shadow property carrying PostgreSQL's own row version. Named for the system column it
    /// maps to, so <see cref="ModelConventions"/>' snake_case sweep leaves it alone.
    /// </summary>
    public const string ConcurrencyTokenProperty = "xmin";

    /// <summary>The table, before <see cref="ModelConventions"/> snake-cases it.</summary>
    private const string Table = "ProposedActions";

    /// <summary>The widest <c>ReviewState</c> name, with room for the four <c>prd.md:69</c> fixes.</summary>
    private const int ReviewStateMaxLength = 32;

    public void Configure(EntityTypeBuilder<ProposedAction> builder)
    {
        builder.ToTable(Table);

        builder.HasKey(proposal => proposal.Id);

        builder.Property(proposal => proposal.ExtractionRunId)
            .IsRequired();

        builder.Property(proposal => proposal.Ordinal)
            .IsRequired();

        builder.Property(proposal => proposal.Description)
            .IsRequired()
            .HasMaxLength(ProposedAction.DescriptionMaxLength);

        // FR-15 — free text, and required rather than nullable: an unstated owner is "", which is
        // a different fact from "we have no field for it".
        builder.Property(proposal => proposal.SuggestedOwner)
            .IsRequired()
            .HasMaxLength(ProposedAction.SuggestedOwnerMaxLength);

        builder.Property(proposal => proposal.SuggestedDueDate);

        builder.Property(proposal => proposal.Confidence)
            .IsRequired();

        builder.Property(proposal => proposal.SourceExcerpt)
            .IsRequired()
            .HasMaxLength(ProposedAction.SourceExcerptMaxLength);

        builder.Property(proposal => proposal.ReviewState)
            .IsRequired()
            .HasMaxLength(ReviewStateMaxLength);

        // AD-20 — PostgreSQL's own row version rather than a column the model has to maintain.
        // NpgsqlConcurrencyTokenConvention recognises this shape and binds it to the `xmin` system
        // column, so no column is added to the table and no migration operation is produced.
        builder.Property<uint>(ConcurrencyTokenProperty)
            .IsRowVersion();

        // AD-20 — `proposed_action(extraction_run_id, ordinal)` is on the unique-index list. It is
        // what makes "AI order" a stored fact: two rows cannot claim the same position in one
        // run's answer, so an ordering by ordinal is total.
        builder.HasIndex(proposal => new { proposal.ExtractionRunId, proposal.Ordinal })
            .IsUnique()
            .HasDatabaseName(RunAndOrdinalIndexName);
    }
}
