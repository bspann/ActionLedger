using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;

namespace ActionLedger.Application.Webhooks;

/// <summary>
/// AD-8 — the one <see cref="IWebhookPayloadBuilder"/>. The payload is built before the commit's
/// single save, so its values come from two places.
/// </summary>
/// <remarks>
/// <para>
/// <strong>In memory:</strong> every Tracked Action field, the proposal's AI values, and the
/// decision's actor and instant. None of those rows is in the database yet, so no query could see
/// them — the decision copy on the proposal is only in the change tracker.
/// </para>
/// <para>
/// <strong>One query:</strong> what is already committed — the Meeting's id and title, reached
/// through the proposal's run, and the owner's and decider's display names. A save-query-save inside
/// a transaction would read the new rows, but the retrying execution strategy cannot replay that
/// shape, so the pipeline stays one save and the builder stays one read.
/// </para>
/// </remarks>
public sealed class WebhookPayloadBuilder : IWebhookPayloadBuilder
{
    /// <inheritdoc />
    /// <exception cref="ArgumentException">The event is not a webhook event, or the proposal is undecided.</exception>
    /// <exception cref="InvalidOperationException">The proposal's run or its Meeting is not committed.</exception>
    public async Task<WebhookEventDto> BuildAsync(
        WebhookEventSource source,
        IReadDb reads,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(reads);

        if (source.Event is not TrackedActionCreated created)
        {
            throw new ArgumentException($"{source.Event.GetType().Name} has no webhook payload.", nameof(source));
        }

        TrackedAction tracked = source.TrackedAction;
        ProposedAction proposal = source.ProposedAction;

        Guid decidedBy = proposal.DecidedByUserId
            ?? throw new ArgumentException("The proposal behind a Tracked Action must be decided.", nameof(source));
        DateTimeOffset decidedAt = proposal.DecidedAt
            ?? throw new ArgumentException("The proposal behind a Tracked Action must be decided.", nameof(source));

        Guid runId = proposal.ExtractionRunId;
        Guid? ownerId = tracked.OwnerUserId;

        // Hoisted for the reason RunsQueries gives: a local composes into one SQL statement, while
        // reads.Query<User>() inside the Select is a client-evaluation failure.
        IQueryable<User> users = reads.Query<User>();

        IQueryable<CommittedFacts> query = reads.Query<ExtractionRun>()
            .Where(run => run.Id == runId)
            .Join(reads.Query<Meeting>(), run => run.MeetingId, meeting => meeting.Id, (run, meeting) => meeting)
            .Select(meeting => new CommittedFacts(
                meeting.Id,
                meeting.Title,
                users.Where(user => user.Id == ownerId).Select(user => user.DisplayName).FirstOrDefault(),
                users.Where(user => user.Id == decidedBy).Select(user => user.DisplayName).FirstOrDefault()));

        CommittedFacts facts = (await reads.ToListAsync(query, cancellationToken)).SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"Extraction run {runId} or its Meeting is not committed, so the webhook payload has no meeting to name.");

        return new WebhookEventDto(
            Guid.CreateVersion7(),
            WebhookEventTypes.ActionApproved,
            created.OccurredAt,
            new WebhookTrackedAction(
                tracked.Id,
                tracked.Description,
                ownerId,
                // Unassigned has no name, whatever a stray query row might say.
                ownerId is null ? null : facts.OwnerName,
                tracked.DueDate,
                tracked.Status,
                facts.MeetingId,
                facts.MeetingTitle),
            new WebhookProposedAction(
                proposal.Id,
                proposal.Description,
                proposal.SuggestedOwner,
                proposal.SuggestedDueDate,
                proposal.Confidence,
                proposal.SourceExcerpt),
            new WebhookReviewDecision(created.Kind, decidedBy, facts.DeciderName, decidedAt));
    }

    /// <summary>The one row the query returns: everything the payload needs that is already committed.</summary>
    private sealed record CommittedFacts(Guid MeetingId, string MeetingTitle, string? OwnerName, string? DeciderName);
}
