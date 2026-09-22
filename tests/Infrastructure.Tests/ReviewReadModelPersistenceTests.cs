using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Extraction;
using ActionLedger.Application.Review;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;
using ActionLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// Story 3.4 against a real PostgreSQL 18: after an Approve, an Edit and a Reject committed through
/// <see cref="DecideProposalHandler"/>, <see cref="RunsQueries.GetAsync"/> answers decision fields
/// that agree with the stored rows and excerpt spans into the stored notes. What this proves over
/// the Application suite is that the read model's extra reads — the notes through the owned
/// navigation, the Tracked Actions by proposal id, the names by user id — translate to SQL.
/// </summary>
[Collection(PostgresFixture.CollectionName)]
public sealed class ReviewReadModelPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateOnly Held = new(2026, 9, 22);

    private static readonly DateTimeOffset Created = new(2026, 9, 22, 14, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DecidedAt = new(2026, 9, 22, 15, 5, 12, TimeSpan.Zero);

    private static readonly DateOnly Proposed = new(2026, 9, 25);

    private const string Notes =
        "Weekly sync.\r\nDana will order the scanners!\nPriya will book the range.\nMarcus to  resurface the lot; soon.";

    private static readonly ExtractionRunMetadata Metrics = new(
        "Fake", "fixture-catalog", "v1", "1", Created, DurationMs: 87, InputTokens: 0, OutputTokens: 0);

    [Fact]
    public async Task The_run_read_carries_decision_fields_and_spans_that_agree_with_the_database()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.CreateDatabaseAsync(nameof(ReviewReadModelPersistenceTests), cancellationToken);

        await using ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff);
        await TestHost.MigrateAsync(services, cancellationToken);

        User actor = await SavedUserAsync(services, "officer", "Olu Officer", cancellationToken);
        User owner = await SavedUserAsync(services, "priya", "Priya Raman", cancellationToken);

        (Guid runId, IReadOnlyList<Guid> ids) = await SavedRunAsync(services, cancellationToken);

        await DecideAsync(services, actor.Id, ids[0], new() { Decision = ReviewVerb.Approve, Description = "Order the scanners.", DueDate = Proposed }, cancellationToken);
        await DecideAsync(services, actor.Id, ids[1], new() { Decision = ReviewVerb.Approve, Description = "Book the range on Friday.", OwnerUserId = owner.Id, DueDate = null }, cancellationToken);
        await DecideAsync(services, actor.Id, ids[2], new() { Decision = ReviewVerb.Reject, Reason = "not an action" }, cancellationToken);

        RunDetailDto run;

        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            // The real read seam, so every read is translated by Npgsql rather than evaluated by
            // LINQ to Objects. The threshold is not under test here.
            IReadDb readDb = scope.ServiceProvider.GetRequiredService<IReadDb>();
            RunsQueries queries = new(readDb, new ProposedActionReadModel(readDb, new FixedSettings()));

            run = await queries.GetAsync(runId, cancellationToken);
        }

        await using AsyncServiceScope check = services.CreateAsyncScope();
        AppDbContext context = check.ServiceProvider.GetRequiredService<AppDbContext>();

        List<TrackedAction> tracked = await context.TrackedActions.AsNoTracking().ToListAsync(cancellationToken);
        List<ProposedAction> stored = await context.Set<ProposedAction>().AsNoTracking().ToListAsync(cancellationToken);

        Assert.Equal(4, run.Proposals.Count);

        foreach (ProposedActionDto proposal in run.Proposals)
        {
            ProposedAction row = stored.Single(candidate => candidate.Id == proposal.Id);

            Assert.Equal(row.ReviewState, proposal.ReviewState);
            Assert.Equal(row.DecidedByUserId, proposal.DecidedByUserId);
            Assert.Equal(row.DecidedAt, proposal.DecidedAt);
            Assert.Equal(row.RejectionReason, proposal.RejectionReason);

            TrackedAction? action = tracked.SingleOrDefault(candidate => candidate.ProposedActionId == proposal.Id);

            Assert.Equal(action?.Id, proposal.TrackedActionId);
            Assert.Equal(action?.Description, proposal.DecidedDescription);
            Assert.Equal(action?.OwnerUserId, proposal.DecidedOwnerUserId);
            Assert.Equal(action?.DueDate, proposal.DecidedDueDate);
        }

        ProposedActionDto approved = run.Proposals[0];
        ProposedActionDto edited = run.Proposals[1];
        ProposedActionDto rejected = run.Proposals[2];
        ProposedActionDto pending = run.Proposals[3];

        Assert.Equal(ReviewState.Approved, approved.ReviewState);
        Assert.Equal("Olu Officer", approved.DecidedByDisplayName);
        Assert.Null(approved.DecidedOwnerDisplayName);
        Assert.Equal(Proposed, approved.DecidedDueDate);

        Assert.Equal(ReviewState.Edited, edited.ReviewState);
        Assert.Equal("Priya Raman", edited.DecidedOwnerDisplayName);
        Assert.Equal("Priya Raman", edited.SuggestedOwnerDisplayName);
        Assert.Equal("Book the range on Friday.", edited.DecidedDescription);

        Assert.Equal(ReviewState.Rejected, rejected.ReviewState);
        Assert.Equal("Olu Officer", rejected.DecidedByDisplayName);
        Assert.Equal("not an action", rejected.RejectionReason);
        Assert.Null(rejected.TrackedActionId);

        Assert.Equal(ReviewState.Pending, pending.ReviewState);
        Assert.Null(pending.DecidedByDisplayName);

        // The spans point into the notes as they were stored, byte-for-byte.
        Assert.Equal("Dana will order the scanners", Span(approved));
        Assert.Equal("Priya will book the range", Span(edited));
        Assert.Equal("Marcus to  resurface the lot; soon", Span(rejected));
        Assert.Null(pending.ExcerptStart);
        Assert.Null(pending.ExcerptLength);
    }

    private static string Span(ProposedActionDto proposal) =>
        Notes.Substring(proposal.ExcerptStart!.Value, proposal.ExcerptLength!.Value);

    private static async Task<(Guid RunId, IReadOnlyList<Guid> ProposalIds)> SavedRunAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        Meeting meeting = Meeting.Create("Weekly sync", Held, ["Dana Whitfield"], Guid.CreateVersion7(), Created);
        MeetingNotes notes = meeting.AttachNotes(Notes, Created);

        ExtractionRun run = ExtractionRun.Start(
            meeting.Id, notes.Id, notes.Sha256, Guid.CreateVersion7(),
            Metrics, ExtractionOutcome.Succeeded, null, warnings: null);

        IReadOnlyList<ActionRevision> revisions = run.AddProposals(
            [
                new ProposedActionDraft("Order the scanners.", "Facilities", Proposed, 0.91, "Dana will order the scanners."),
                new ProposedActionDraft("Book the range.", "priya raman", null, 0.62, "Priya will book the range."),
                new ProposedActionDraft("Resurface the lot.", string.Empty, null, 0.55, "marcus to resurface the lot soon"),
                new ProposedActionDraft("Seeded elsewhere.", string.Empty, null, 0.8, "Nobody said this."),
            ],
            Created);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IMeetingRepository>().Add(meeting);
        scope.ServiceProvider.GetRequiredService<IExtractionRunRepository>().Add(run);
        scope.ServiceProvider.GetRequiredService<IActionRevisionRepository>().AddRange(revisions);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        return (run.Id, [.. run.Proposals.Select(proposal => proposal.Id)]);
    }

    private static async Task DecideAsync(
        IServiceProvider services,
        Guid actorUserId,
        Guid proposalId,
        DecideProposalCommand command,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();

        DecideProposalHandler handler = new(
            scope.ServiceProvider.GetRequiredService<IExtractionRunRepository>(),
            scope.ServiceProvider.GetRequiredService<IActionRepository>(),
            scope.ServiceProvider.GetRequiredService<IActionRevisionRepository>(),
            scope.ServiceProvider.GetRequiredService<IReadDb>(),
            new FixedCurrentUser(actorUserId),
            new FixedClock(DecidedAt),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());

        await handler.HandleAsync(proposalId, command, cancellationToken);
    }

    private static async Task<User> SavedUserAsync(
        IServiceProvider services,
        string username,
        string displayName,
        CancellationToken cancellationToken)
    {
        User user = User.Register(username, displayName, new string('h', 60), Role.ActionOfficer, Created);

        await using AsyncServiceScope scope = services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IUserRepository>().Add(user);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);

        return user;
    }

    private sealed class FixedCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId { get; } = userId;

        public string DisplayName => "Olu Officer";

        public Role Role => Role.ActionOfficer;
    }

    private sealed class FixedSettings : IExtractionSettings
    {
        public double LowConfidenceThreshold => 0.70;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
