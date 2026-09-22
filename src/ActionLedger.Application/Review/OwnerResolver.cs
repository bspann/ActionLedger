using ActionLedger.Application.Users;

namespace ActionLedger.Application.Review;

/// <summary>
/// AD-9 / ADR-006 — the only owner matcher in the solution. A pure function, so the Review Screen,
/// Run Detail, and Story 3.2's change test can never disagree about whether a suggested name is
/// somebody.
/// </summary>
/// <remarks>
/// The AI writes free text, never a foreign key. This turns that text into a <em>suggestion</em> of
/// a User id for a picker to pre-select; the proposal keeps its free text either way (FR-15,
/// <c>prd.md:253</c>), and nothing here assigns an owner.
/// </remarks>
public static class OwnerResolver
{
    /// <summary>
    /// The User whose display name equals <paramref name="suggestedOwner"/>, ignoring case, or
    /// <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>null</c> covers three cases on purpose: the suggestion is blank, it matches nobody, and
    /// it matches more than one. The ambiguous case has no defensible pick — returning the first of
    /// two people who share a display name would pre-select a name the reviewer never chose — and
    /// FR-15 already allows an empty picker, so "we do not know" is a state the UI can render.
    /// </para>
    /// <para>
    /// The suggestion is trimmed because it came out of prose. The display names are not, because
    /// they are stored trimmed.
    /// </para>
    /// </remarks>
    /// <param name="suggestedOwner">The name the notes used. May be <c>null</c>, empty or padded.</param>
    /// <param name="users">The roster to match against.</param>
    public static Guid? Match(string? suggestedOwner, IReadOnlyList<UserSummaryDto> users)
    {
        ArgumentNullException.ThrowIfNull(users);

        string name = (suggestedOwner ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            return null;
        }

        Guid? matched = null;

        foreach (UserSummaryDto user in users)
        {
            if (!string.Equals(user.DisplayName, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (matched is not null)
            {
                // Two people answer to this name. Neither is the answer.
                return null;
            }

            matched = user.Id;
        }

        return matched;
    }
}
