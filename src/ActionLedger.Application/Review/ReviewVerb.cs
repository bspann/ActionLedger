namespace ActionLedger.Application.Review;

/// <summary>
/// What the reviewer pressed: the client's <em>intent</em>, and nothing more. Published as the
/// string it is serialized as.
/// </summary>
/// <remarks>
/// There is deliberately no <c>Edit</c>. The Review Screen's Edit button is a UI mode that ends in
/// an Approve, and <see cref="DecideProposalHandler"/> alone decides whether that approval is
/// <c>Approved</c> or <c>Edited</c> by diffing what was sent against the proposal
/// (reconcile-inputs.md:64). A two-value verb is what lets a reason-less Reject and an unchanged
/// Approve be told apart at all.
/// </remarks>
public enum ReviewVerb
{
    /// <summary>Take the proposal — as it stands, or with the sent edits. Creates a Tracked Action.</summary>
    Approve,

    /// <summary>Refuse the proposal, with an optional reason. Creates nothing.</summary>
    Reject,
}
