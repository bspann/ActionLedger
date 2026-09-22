namespace ActionLedger.Application.Review;

/// <summary>
/// The body <c>POST /api/v1/proposed-actions/{id}/decision</c> binds: the reviewer's verb and the
/// values it applies to.
/// </summary>
/// <remarks>
/// <para>
/// There is no actor and no instant here, and no member that could stand in for either (AD-12,
/// FR-25): both come from <c>ICurrentUser</c> and <c>IClock</c>. <c>ActorIntegrityTests</c> fails
/// the build if one appears. <see cref="OwnerUserId"/> names the <em>target</em> of the work, not
/// the caller.
/// </para>
/// <para>
/// There is no kind either. The client never says Approved or Edited; the handler works that out
/// from a diff (AD-3).
/// </para>
/// </remarks>
public sealed record DecideProposalCommand
{
    /// <summary>Approve or Reject.</summary>
    /// <remarks>
    /// A C# <c>required</c> member rather than <c>[Required]</c> on a <c>ReviewVerb?</c>, which is
    /// how <c>CreateMeetingCommand</c> keeps its date from defaulting. The serializer refuses a
    /// body that omits a <c>required</c> member, so an omitted verb is model binding's 400 rather
    /// than silently becoming <see cref="ReviewVerb.Approve"/>, the enum's first member — and it
    /// publishes as a plain reference to <see cref="ReviewVerb"/>. A nullable enum would publish
    /// as <c>oneOf: [null, ReviewVerb]</c>, a shape <c>OpenApiSetup</c>'s required-member
    /// transformer never sees, and the contract would offer the <c>null</c> the server refuses.
    /// </remarks>
    public required ReviewVerb Decision { get; init; }

    /// <summary>
    /// The description to track, 1–500 characters and not blank. Required to approve; ignored on a
    /// rejection. Send the proposal's own description, unchanged, to keep it.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The User who will own the work, or <c>null</c> for Unassigned. Must be a User on the
    /// roster. Recorded exactly as sent — the server's owner match is a pre-selection, never an
    /// assignment. Ignored on a rejection.
    /// </summary>
    public Guid? OwnerUserId { get; init; }

    /// <summary>The due date to track, or <c>null</c> for none. Ignored on a rejection.</summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>
    /// A rejection's optional reason, at most 500 characters after trimming. Refused on an
    /// approval.
    /// </summary>
    public string? Reason { get; init; }
}
