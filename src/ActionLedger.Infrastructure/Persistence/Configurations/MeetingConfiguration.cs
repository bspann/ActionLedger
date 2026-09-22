using ActionLedger.Domain.Meetings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActionLedger.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>meetings</c> table and the <c>meeting_notes</c> table it owns. Column names, the
/// snake_case table names, <c>date</c> for the meeting day, <c>timestamptz</c> for the instants,
/// <c>text[]</c> for the attendees, and <c>ValueGeneratedNever</c> on the keys all come from
/// <see cref="ModelConventions"/>; what is here is specific to this aggregate.
/// </summary>
/// <remarks>
/// <see cref="MeetingNotes"/> is configured as an <em>owned</em> type rather than as a second
/// root. That is AD-3 expressed in the model: notes have no independent lifetime, they are always
/// loaded with their Meeting — so <see cref="Meeting.HasNotes"/> can never answer from a
/// navigation somebody forgot to include — and they cascade with it.
/// </remarks>
internal sealed class MeetingConfiguration : IEntityTypeConfiguration<Meeting>
{
    /// <summary>The name of the AD-20 unique index on the Meeting's natural key, so a test can name it.</summary>
    public const string TitleAndDateIndexName = "ix_meetings_title_meeting_date";

    /// <summary>
    /// The name of the AD-20 unique index that makes "notes attach exactly once" true under
    /// concurrency, not only under the aggregate's own rule.
    /// </summary>
    public const string NotesMeetingIndexName = "ix_meeting_notes_meeting_id";

    /// <summary>
    /// The shadow property carrying PostgreSQL's own row version. Named for the system column it
    /// maps to, so <see cref="ModelConventions"/>' snake_case sweep leaves it alone.
    /// </summary>
    public const string ConcurrencyTokenProperty = "xmin";

    /// <summary>The owned notes' table, before <see cref="ModelConventions"/> snake-cases it.</summary>
    private const string NotesTable = "MeetingNotes";

    public void Configure(EntityTypeBuilder<Meeting> builder)
    {
        builder.HasKey(meeting => meeting.Id);

        builder.Property(meeting => meeting.Title)
            .IsRequired()
            .HasMaxLength(Meeting.TitleMaxLength);

        builder.Property(meeting => meeting.MeetingDate)
            .IsRequired();

        // The provider maps this to text[]; only the nullability is this configuration's business.
        builder.Property(meeting => meeting.Attendees)
            .IsRequired();

        builder.Property(meeting => meeting.CreatedByUserId)
            .IsRequired();

        builder.Property(meeting => meeting.CreatedAt)
            .IsRequired();

        // AD-20 — Meeting is one of the four roots with a state machine, so it carries PostgreSQL's
        // own row version as its concurrency token rather than a column the model has to maintain.
        //
        // `UseXminAsConcurrencyToken()` is how earlier Npgsql providers spelled this; it was
        // removed in Npgsql 9. The replacement is a uint concurrency token generated on add and
        // update, which NpgsqlConcurrencyTokenConvention recognises and binds to the `xmin` system
        // column — so no column is added to the table and no migration operation is produced.
        builder.Property<uint>(ConcurrencyTokenProperty)
            .IsRowVersion();

        // AD-20 — `meeting(title, meeting_date)` is on the unique-index list. Title is trimmed by
        // Meeting.Create, or "Standup" and "Standup " would be two meetings to this index.
        builder.HasIndex(meeting => new { meeting.Title, meeting.MeetingDate })
            .IsUnique()
            .HasDatabaseName(TitleAndDateIndexName);

        // Derived from the navigation; not a column.
        builder.Ignore(meeting => meeting.HasNotes);

        // Domain events are raised in memory and drained inside the commit; they are not a column.
        builder.Ignore(meeting => meeting.DomainEvents);

        builder.OwnsOne(meeting => meeting.Notes, notes =>
        {
            notes.ToTable(NotesTable);

            notes.HasKey(note => note.Id);
            notes.WithOwner().HasForeignKey(note => note.MeetingId);

            notes.Property(note => note.Text)
                .IsRequired()
                .HasMaxLength(MeetingNotes.TextMaxLength);

            notes.Property(note => note.Sha256)
                .IsRequired()
                .HasMaxLength(MeetingNotes.Sha256Length)
                .IsFixedLength();

            notes.Property(note => note.SavedAt)
                .IsRequired();

            // AD-20 — `meeting_notes(meeting_id)` is on the unique-index list, and it is the
            // backstop for the concurrent case: the aggregate refuses a second AttachNotes, and
            // this refuses a second row when two writers each read a Meeting that had none.
            notes.HasIndex(note => note.MeetingId)
                .IsUnique()
                .HasDatabaseName(NotesMeetingIndexName);
        });

        // A Meeting without notes is the ordinary state, not a broken row.
        builder.Navigation(meeting => meeting.Notes).IsRequired(false);
    }
}
