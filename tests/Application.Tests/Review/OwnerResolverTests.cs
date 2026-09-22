using ActionLedger.Application.Review;
using ActionLedger.Application.Users;
using ActionLedger.Domain.Users;
using Xunit;

namespace ActionLedger.Application.Tests.Review;

/// <summary>
/// AD-9 / ADR-006 — the one owner matcher. It is a pure function, so every row of the story's
/// resolver matrix is a direct call rather than something inferred from a screen.
/// </summary>
public sealed class OwnerResolverTests
{
    private static readonly UserSummaryDto Dana = new(Guid.CreateVersion7(), "Dana Whitfield", Role.ActionOfficer);

    private static readonly UserSummaryDto Priya = new(Guid.CreateVersion7(), "Priya Raman", Role.Lead);

    private static readonly UserSummaryDto[] Roster = [Dana, Priya];

    [Fact]
    public void An_exact_name_matches_that_user() =>
        Assert.Equal(Dana.Id, OwnerResolver.Match("Dana Whitfield", Roster));

    [Theory]
    [InlineData("dana whitfield")]
    [InlineData("DANA WHITFIELD")]
    [InlineData("DaNa WhItFiElD")]
    public void The_match_ignores_case(string suggestion) =>
        // Notes are prose. "dana whitfield" in a hurried paste is the same person.
        Assert.Equal(Dana.Id, OwnerResolver.Match(suggestion, Roster));

    [Theory]
    [InlineData("  Dana Whitfield")]
    [InlineData("Dana Whitfield  ")]
    [InlineData("\t Dana Whitfield \r\n")]
    public void The_suggestion_is_trimmed_before_it_is_matched(string suggestion) =>
        Assert.Equal(Dana.Id, OwnerResolver.Match(suggestion, Roster));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void A_blank_suggestion_matches_nobody(string? suggestion) =>
        // FR-15 — an unstated owner is "" on the proposal, and "" is not a person.
        Assert.Null(OwnerResolver.Match(suggestion, Roster));

    [Theory]
    [InlineData("Facilities")]
    [InlineData("Dana")]
    [InlineData("Dana Whitfeld")]
    public void A_name_no_user_answers_to_matches_nobody(string suggestion) =>
        // Partial and misspelled names deliberately do not match: a near-miss pre-selection is a
        // wrong owner the reviewer has to notice, which is worse than an empty picker.
        Assert.Null(OwnerResolver.Match(suggestion, Roster));

    [Fact]
    public void A_name_two_users_share_matches_nobody()
    {
        UserSummaryDto[] namesakes =
        [
            new(Guid.CreateVersion7(), "Dana Whitfield", Role.ActionOfficer),
            new(Guid.CreateVersion7(), "Dana Whitfield", Role.Lead),
        ];

        // There is no defensible pick. Returning the first would pre-select a person the reviewer
        // never chose, and FR-15 already allows an empty picker.
        Assert.Null(OwnerResolver.Match("Dana Whitfield", namesakes));
    }

    [Fact]
    public void An_ambiguity_that_is_only_ambiguous_case_insensitively_still_matches_nobody()
    {
        UserSummaryDto[] namesakes =
        [
            new(Guid.CreateVersion7(), "Dana Whitfield", Role.ActionOfficer),
            new(Guid.CreateVersion7(), "dana whitfield", Role.Lead),
        ];

        // The comparison that finds the match is the comparison that has to find the collision,
        // or an ordinal duplicate check would wave this one through.
        Assert.Null(OwnerResolver.Match("Dana Whitfield", namesakes));
    }

    [Fact]
    public void An_empty_roster_matches_nobody() =>
        Assert.Null(OwnerResolver.Match("Dana Whitfield", []));

    [Fact]
    public void A_second_user_with_a_different_name_does_not_disturb_the_match() =>
        Assert.Equal(Priya.Id, OwnerResolver.Match("priya raman", Roster));
}
