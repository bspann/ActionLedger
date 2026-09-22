using ActionLedger.Domain.Actions;

namespace ActionLedger.Domain.Extraction;

/// <summary>
/// What <see cref="ProposedAction.Decide"/> produced: the new Tracked Action, if any, and the
/// revisions that audit the decision.
/// </summary>
/// <remarks>
/// Both are returned rather than held, for the reason <c>ExtractionRun.AddProposals</c> returns its
/// revisions: a Tracked Action and an <see cref="ActionRevision"/> are their own roots, and the
/// handler hands them to repositories the proposal must not know about.
/// </remarks>
/// <param name="Kind">The decision that was made.</param>
/// <param name="TrackedAction">The new Tracked Action for an approval or edit; <c>null</c> for a rejection.</param>
/// <param name="Revisions">The ReviewDecision revision first, then one FieldEdit per changed field.</param>
public sealed record DecisionResult(
    DecisionKind Kind,
    TrackedAction? TrackedAction,
    IReadOnlyList<ActionRevision> Revisions);
