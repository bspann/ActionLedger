using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace ActionLedger.Infrastructure.Persistence;

/// <summary>
/// The one <c>DbContext</c>. It owns the conventions every later aggregate inherits (AD-10) and
/// is the single place a write becomes SQL (AD-20).
/// </summary>
/// <remarks>
/// <para>
/// <c>DbSet</c> names are plural because the naming convention turns the table name EF derives
/// from them into snake_case: <c>Users</c> becomes <c>users</c>. Entity types stay singular.
/// </para>
/// <para>
/// AD-17 — this context never migrates itself. Nothing here calls <c>Migrate</c>, and the api
/// never does either: schema ships only through the bundle the api image build produces.
/// </para>
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>The people who sign in and whose names are stamped on every write (AD-12).</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// The meetings an Action Officer captures. Each owns its write-once <c>MeetingNotes</c>
    /// (AD-3, ADR-002), which has no <c>DbSet</c> of its own because it has no lifetime of its own.
    /// </summary>
    public DbSet<Meeting> Meetings => Set<Meeting>();

    /// <summary>
    /// Every attempt to turn a Meeting's notes into proposals, Succeeded and Failed alike (AD-6,
    /// AD-11). Each owns its <c>ProposedAction</c> list, which has no <c>DbSet</c> of its own
    /// because it has no lifetime of its own.
    /// </summary>
    public DbSet<ExtractionRun> ExtractionRuns => Set<ExtractionRun>();

    /// <summary>
    /// The append-only audit rows (AD-7, ADR-004). Its own root with no navigation from any
    /// aggregate, so it needs a <c>DbSet</c> even though only aggregates ever create one.
    /// </summary>
    public DbSet<ActionRevision> ActionRevisions => Set<ActionRevision>();

    /// <summary>
    /// The work a human decision created (AD-3, AD-4). Only <c>ProposedAction.Decide</c> mints
    /// one; this set is how a repository adds it and a query reads it.
    /// </summary>
    public DbSet<TrackedAction> TrackedActions => Set<TrackedAction>();

    /// <inheritdoc />
    /// <remarks>
    /// Translation lives here rather than in <c>UnitOfWork</c> so every save path is covered —
    /// including the seeder's, which commits through the same context. No EF Core exception
    /// escapes this ring (AD-1).
    /// </remarks>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (ConcurrencyTranslation.Translate(exception) is { } conflict)
        {
            throw conflict;
        }
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // AD-20 puts a `xmin` concurrency token on ProposedAction, TrackedAction, Meeting, and
        // OutboxMessage — the four roots with a state machine. `User` is not one of them and does
        // not get one, and neither does `ExtractionRun`: it is inserted once and never updated.
        // `Meeting`, `ProposedAction` and `TrackedAction` now have theirs, set by their own
        // configurations; OutboxMessage arrives with its story and sets its own the same way:
        //
        //     builder.Property<uint>("xmin").IsRowVersion();
        //
        // NpgsqlConcurrencyTokenConvention recognises that shape and binds it to PostgreSQL's
        // `xmin` system column, so no column is added and no migration operation is produced.
        // `UseXminAsConcurrencyToken()` is how earlier providers spelled this; it does not exist
        // in Npgsql.EntityFrameworkCore.PostgreSQL 10.

        ModelConventions.Apply(modelBuilder);
    }
}
