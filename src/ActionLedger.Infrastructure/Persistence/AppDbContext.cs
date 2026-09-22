using System.Text.Json;
using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Webhooks;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Common;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;
using ActionLedger.Domain.Webhooks;
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
/// <para>
/// AD-8 / NFR-3 — every async save runs the outbox pipeline before it writes: each webhook event a
/// tracked root raised becomes one Pending <see cref="OutboxMessage"/> per active matching
/// subscription, added to the same <c>SaveChanges</c> and therefore to the same transaction as the
/// change that raised it. There is no second save and no explicit transaction.
/// </para>
/// </remarks>
public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    IClock clock,
    IWebhookPayloadBuilder payloadBuilder) : DbContext(options)
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

    /// <summary>
    /// AD-8 — the receivers that asked for webhook events. Nothing in this story writes one; the
    /// outbox pipeline reads the active ones inside every save.
    /// </summary>
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();

    /// <summary>
    /// AD-8 / ADR-005 — the deliveries owed, written by the outbox pipeline in the same transaction as
    /// the change that raised their event. Epic 5's dispatcher drains them.
    /// </summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The core every async save path reaches — EF's own <c>SaveChangesAsync(CancellationToken)</c>
    /// forwards here — so the outbox pipeline and the concurrency translation cannot be skipped.
    /// </para>
    /// <para>
    /// Translation lives here rather than in <c>UnitOfWork</c> so every save path is covered —
    /// including the seeder's, which commits through the same context. No EF Core exception
    /// escapes this ring (AD-1).
    /// </para>
    /// </remarks>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        SaveCoreAsync(skipOutbox: false, acceptAllChangesOnSuccess, cancellationToken);

    /// <summary>
    /// A save that may skip the outbox pipeline. Reserved for the AD-21 seeder, whose decisions are
    /// history rather than news: an Architecture test holds every other caller out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cancellation token comes first and the flag second, so a bare <c>bool</c> in first
    /// position can never bind here: <c>SaveChangesAsync(true, token)</c> is EF's
    /// <c>acceptAllChangesOnSuccess</c> overload and runs the pipeline. Suppressing takes the
    /// named argument <c>suppressOutbox: true</c>. With the flag left at its default this is an
    /// ordinary save through the pipeline, exactly as EF's own <c>SaveChangesAsync(CancellationToken)</c>.
    /// </para>
    /// <para>
    /// <c>internal</c>, so nothing outside Infrastructure can reach it at all. Events are cleared
    /// after a suppressed save exactly as after any other.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <param name="suppressOutbox"><c>true</c> writes no outbox rows for the events this save carries.</param>
    internal Task<int> SaveChangesAsync(CancellationToken cancellationToken = default, bool suppressOutbox = false) =>
        SaveCoreAsync(skipOutbox: suppressOutbox, acceptAllChangesOnSuccess: true, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Refused, so no path skips the outbox pipeline. Nothing in the solution saves synchronously,
    /// and the pipeline's reads are async.
    /// </remarks>
    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException(
            "AppDbContext saves only through SaveChangesAsync, which runs the outbox pipeline (AD-8, NFR-3).");

    private async Task<int> SaveCoreAsync(bool skipOutbox, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        // Snapshotted before anything is added, so the roots cleared afterwards are exactly the ones
        // whose events this save carried.
        List<AggregateRoot> raised =
        [
            .. ChangeTracker.Entries<AggregateRoot>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
                .Where(entry => entry.Entity.DomainEvents.Count > 0)
                .Select(entry => entry.Entity),
        ];

        List<OutboxMessage> enqueued = skipOutbox
            ? []
            : await EnqueueOutboxAsync(raised, cancellationToken);

        int written;

        try
        {
            written = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (Exception exception)
        {
            // The events stay, so a caller that saves again derives its rows again — once. Leaving
            // these attached as well would enqueue every delivery twice.
            foreach (OutboxMessage message in enqueued)
            {
                Entry(message).State = EntityState.Detached;
            }

            if (ConcurrencyTranslation.Translate(exception) is { } conflict)
            {
                throw conflict;
            }

            throw;
        }

        foreach (AggregateRoot root in raised)
        {
            root.ClearDomainEvents();
        }

        return written;
    }

    /// <summary>
    /// The outbox pipeline: one Pending row per webhook event per active subscription that asked
    /// for its type. The rows are added to this context only once every one has been built, so a
    /// build or query that throws part-way leaves nothing staged; the one save then writes them.
    /// </summary>
    private async Task<List<OutboxMessage>> EnqueueOutboxAsync(
        IReadOnlyList<AggregateRoot> raised,
        CancellationToken cancellationToken)
    {
        List<OutboxMessage> enqueued = [];
        Dictionary<string, IReadOnlyList<Guid>> subscribersByType = new(StringComparer.Ordinal);
        ReadDb reads = new(this);

        // AD-15 — one instant per save, so every row this commit writes shares it.
        DateTimeOffset now = clock.UtcNow;

        foreach (AggregateRoot root in raised)
        {
            foreach (DomainEvent domainEvent in root.DomainEvents)
            {
                if (WebhookEventTypes.For(domainEvent) is not { } eventType)
                {
                    continue;
                }

                WebhookEventDto payload = await payloadBuilder.BuildAsync(
                    SourceFor(root, domainEvent), reads, cancellationToken);

                string json = JsonSerializer.Serialize(payload, WebhookJson.SerializerOptions);

                if (!subscribersByType.TryGetValue(eventType, out IReadOnlyList<Guid>? subscribers))
                {
                    subscribers = await Set<WebhookSubscription>()
                        .AsNoTracking()
                        .Where(subscription => subscription.IsActive && subscription.EventTypes.Contains(eventType))
                        .OrderBy(subscription => subscription.Id)
                        .Select(subscription => subscription.Id)
                        .ToListAsync(cancellationToken);

                    subscribersByType[eventType] = subscribers;
                }

                enqueued.AddRange(subscribers.Select(subscriptionId =>
                    OutboxMessage.Enqueue(subscriptionId, eventType, payload.EventId, json, now)));
            }
        }

        OutboxMessages.AddRange(enqueued);

        return enqueued;
    }

    /// <summary>
    /// The tracked entities a webhook event concerns. The decided proposal is a child of its run, so
    /// it is found among the tracked proposals: the decision's commit always loaded it.
    /// </summary>
    private WebhookEventSource SourceFor(AggregateRoot root, DomainEvent domainEvent)
    {
        if (domainEvent is not TrackedActionCreated created || root is not TrackedAction tracked)
        {
            throw new InvalidOperationException(
                $"{domainEvent.GetType().Name} raised on {root.GetType().Name} is a webhook event the outbox pipeline cannot source.");
        }

        ProposedAction proposal = ChangeTracker.Entries<ProposedAction>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(candidate => candidate.Id == created.ProposedActionId)
            ?? throw new InvalidOperationException(
                $"Proposed Action {created.ProposedActionId} is not tracked, so the webhook payload for Tracked Action {tracked.Id} cannot carry its decision.");

        return new WebhookEventSource(domainEvent, tracked, proposal);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // AD-20 puts a `xmin` concurrency token on ProposedAction, TrackedAction, Meeting, and
        // OutboxMessage — the four roots with a state machine. `User` is not one of them and does
        // not get one, and neither does `ExtractionRun`: it is inserted once and never updated.
        // All four now have theirs, each set by its own configuration in the same shape:
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
