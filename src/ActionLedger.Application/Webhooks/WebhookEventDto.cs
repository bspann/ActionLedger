using ActionLedger.Domain.Actions;
using ActionLedger.Domain.Extraction;

namespace ActionLedger.Application.Webhooks;

/// <summary>
/// AD-8 — the body every webhook delivery carries, field for field the PRD addendum's integrator
/// contract. It is serialized once per event and stored on each outbox row as <c>jsonb</c>, which
/// keeps its meaning but may reorder keys; the dispatcher sends and signs what it reads back.
/// </summary>
/// <remarks>
/// The nested records are this contract's own rather than the Review Screen's DTOs, so a change to
/// what the UI reads can never change what an integrator receives.
/// </remarks>
/// <param name="EventId">The event's id: every row for the event carries it, and it is stable across retries.</param>
/// <param name="EventType">The webhook event type, such as <c>action.approved</c>.</param>
/// <param name="OccurredAt">When the aggregate decided it, in UTC.</param>
/// <param name="TrackedAction">The Tracked Action the approval created.</param>
/// <param name="ProposedAction">The proposal as the AI made it; never the edited values.</param>
/// <param name="ReviewDecision">Who decided, how, and when.</param>
public sealed record WebhookEventDto(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    WebhookTrackedAction TrackedAction,
    WebhookProposedAction ProposedAction,
    WebhookReviewDecision ReviewDecision);

/// <summary>The Tracked Action, with its owner's and its meeting's names resolved.</summary>
/// <param name="Id">The Tracked Action's id.</param>
/// <param name="Description">What is to be done, as approved.</param>
/// <param name="OwnerId">The owning User, or <c>null</c> for Unassigned.</param>
/// <param name="OwnerName">The owner's display name, or <c>null</c> for Unassigned.</param>
/// <param name="DueDate">When it is due, or <c>null</c>.</param>
/// <param name="Status">Where the work stands.</param>
/// <param name="MeetingId">The Meeting whose notes it came from.</param>
/// <param name="MeetingTitle">That Meeting's title.</param>
public sealed record WebhookTrackedAction(
    Guid Id,
    string Description,
    Guid? OwnerId,
    string? OwnerName,
    DateOnly? DueDate,
    ActionStatus Status,
    Guid MeetingId,
    string MeetingTitle);

/// <summary>The proposal exactly as the AI returned it.</summary>
/// <param name="Id">The proposal's id.</param>
/// <param name="Description">The AI's wording.</param>
/// <param name="SuggestedOwner">The AI's free-text owner; <c>""</c> when the notes named nobody.</param>
/// <param name="SuggestedDueDate">The AI's due date, or <c>null</c>.</param>
/// <param name="Confidence">0–1 inclusive.</param>
/// <param name="SourceExcerpt">The sentence the proposal quotes.</param>
public sealed record WebhookProposedAction(
    Guid Id,
    string Description,
    string SuggestedOwner,
    DateOnly? SuggestedDueDate,
    double Confidence,
    string SourceExcerpt);

/// <summary>The human decision behind the Tracked Action.</summary>
/// <param name="Kind"><see cref="DecisionKind.Approved"/> or <see cref="DecisionKind.Edited"/>.</param>
/// <param name="ByUserId">The User who decided.</param>
/// <param name="ByUserName">That User's display name.</param>
/// <param name="At">When it was decided, in UTC.</param>
public sealed record WebhookReviewDecision(
    DecisionKind Kind,
    Guid ByUserId,
    string? ByUserName,
    DateTimeOffset At);
