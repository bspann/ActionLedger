using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActionLedger.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>extraction_runs</c> table (AD-6). The snake_case table and column names,
/// <c>timestamptz</c> for the instants, the string <c>outcome</c>, and <c>ValueGeneratedNever</c>
/// on the key all come from <see cref="ModelConventions"/>; what is here is specific to this root.
/// </summary>
/// <remarks>
/// <para>
/// AD-20 — a run carries <em>no</em> concurrency token. The four token-carrying types are
/// <c>ProposedAction</c>, <c>TrackedAction</c>, <c>Meeting</c> and <c>OutboxMessage</c>, because
/// those are the rows a second actor can race. A run is inserted once and never updated, so a
/// token on it would cost a column-shaped claim in the snapshot and protect nothing.
/// </para>
/// <para>
/// The proposals are a <c>HasMany</c> rather than an <c>OwnsMany</c>, although AD-3 has the run
/// own them. Owned collections have no <c>DbSet</c> and cannot be reached through
/// <c>context.Set&lt;T&gt;()</c>, and <c>ProposedActionReadModel</c> — the AD-9 single producer of
/// <c>ProposedActionDto</c> — reads them through <c>IReadDb.Query&lt;ProposedAction&gt;()</c>,
/// which is exactly that call. The cascade below and the <c>internal</c> constructor on
/// <c>ProposedAction</c> are what keep the ownership real: nothing outside the aggregate can mint
/// one, and deleting a run deletes its proposals.
/// </para>
/// <para>
/// <c>MeetingId</c> and <c>MeetingNotesId</c> are plain ids with no navigation and no foreign key.
/// They cross an aggregate boundary, and AD-3 has aggregates reference each other by id.
/// </para>
/// </remarks>
internal sealed class ExtractionRunConfiguration : IEntityTypeConfiguration<ExtractionRun>
{
    /// <summary>
    /// The name of the index behind the Meeting List's run count, so a test can name it.
    /// </summary>
    public const string MeetingIndexName = "ix_extraction_runs_meeting_id";

    public void Configure(EntityTypeBuilder<ExtractionRun> builder)
    {
        builder.HasKey(run => run.Id);

        builder.Property(run => run.MeetingId)
            .IsRequired();

        builder.Property(run => run.MeetingNotesId)
            .IsRequired();

        // AD-5 — a copy of the notes' own digest, so "this run read those bytes" is a comparison.
        builder.Property(run => run.NotesSha256)
            .IsRequired()
            .HasMaxLength(MeetingNotes.Sha256Length)
            .IsFixedLength();

        builder.Property(run => run.StartedByUserId)
            .IsRequired();

        builder.Property(run => run.Provider)
            .IsRequired()
            .HasMaxLength(ExtractionRunMetadata.ProviderMaxLength);

        builder.Property(run => run.Model)
            .IsRequired()
            .HasMaxLength(ExtractionRunMetadata.ModelMaxLength);

        builder.Property(run => run.PromptVersion)
            .IsRequired()
            .HasMaxLength(ExtractionRunMetadata.PromptVersionMaxLength);

        builder.Property(run => run.SchemaVersion)
            .IsRequired()
            .HasMaxLength(ExtractionRunMetadata.SchemaVersionMaxLength);

        builder.Property(run => run.StartedAt)
            .IsRequired();

        builder.Property(run => run.DurationMs)
            .IsRequired();

        // FR-6 — tokens are non-null integers, `0` for the Fake, so Run Detail renders "0" rather
        // than a blank. A nullable column would make "zero" and "unknown" the same row.
        builder.Property(run => run.InputTokens)
            .IsRequired();

        builder.Property(run => run.OutputTokens)
            .IsRequired();

        builder.Property(run => run.Outcome)
            .IsRequired()
            .HasMaxLength(OutcomeMaxLength);

        // Null exactly when the run succeeded. The aggregate holds the two in step.
        builder.Property(run => run.FailureReason)
            .HasMaxLength(ExtractionRun.FailureReasonMaxLength);

        // The provider maps this to text[]; only the nullability is this configuration's business.
        // Empty is the ordinary state, never null (AD-6).
        builder.Property(run => run.Warnings)
            .IsRequired();

        // Domain events are raised in memory and drained inside the commit; they are not a column.
        builder.Ignore(run => run.DomainEvents);

        // NFR-2 — `MeetingsQueries.ListAsync` runs a correlated count of this column once per row
        // of every Meeting page. Without an index that is a sequential scan of extraction_runs per
        // row, which is the one list read every signed-in user hits first. Not unique: AD-5 puts
        // any number of runs on a Meeting.
        builder.HasIndex(run => run.MeetingId)
            .HasDatabaseName(MeetingIndexName);

        builder.HasMany(run => run.Proposals)
            .WithOne()
            .HasForeignKey(proposal => proposal.ExtractionRunId)
            .OnDelete(DeleteBehavior.Cascade);

        // The list is private; the read-only property in front of it is not something EF can
        // write through, so the backing field is named explicitly rather than left to convention.
        builder.Metadata
            .FindNavigation(nameof(ExtractionRun.Proposals))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }

    /// <summary>The widest <c>ExtractionOutcome</c> name, with room for the ones nothing adds yet.</summary>
    private const int OutcomeMaxLength = 32;
}
