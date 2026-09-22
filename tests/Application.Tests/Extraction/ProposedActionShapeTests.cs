using ActionLedger.Application.Ai;
using ActionLedger.Domain.Extraction;
using Xunit;

namespace ActionLedger.Application.Tests.Extraction;

/// <summary>
/// The column bounds a proposal is stored under are the bounds the committed extraction schema
/// validates against. Nothing else holds the two together.
/// </summary>
/// <remarks>
/// <para>
/// <c>ProposedAction</c> declares its own constants because AD-1 forbids Domain seeing
/// <c>ExtractionOutputValidator</c> at all, and <c>ProposedActionConfiguration</c> derives the
/// column lengths from those. So the chain is validator → (this test) → Domain constant → column.
/// </para>
/// <para>
/// This assembly is the innermost one that sees both rings, which is why the assertion lives here
/// rather than in <c>Domain.Tests</c>. Raise <c>maxLength</c> in the committed schema without
/// raising it here and a proposal the validator accepts would be refused by the aggregate — or,
/// worse, accepted by the aggregate and rejected by PostgreSQL mid-commit.
/// </para>
/// </remarks>
public sealed class ProposedActionShapeTests
{
    [Fact]
    public void The_stored_bounds_are_the_validators_bounds()
    {
        Assert.Equal(ExtractionOutputValidator.DescriptionMinLength, ProposedAction.DescriptionMinLength);
        Assert.Equal(ExtractionOutputValidator.DescriptionMaxLength, ProposedAction.DescriptionMaxLength);
        Assert.Equal(ExtractionOutputValidator.SuggestedOwnerMaxLength, ProposedAction.SuggestedOwnerMaxLength);
        Assert.Equal(ExtractionOutputValidator.SourceExcerptMinLength, ProposedAction.SourceExcerptMinLength);
        Assert.Equal(ExtractionOutputValidator.SourceExcerptMaxLength, ProposedAction.SourceExcerptMaxLength);
        Assert.Equal(ExtractionOutputValidator.ConfidenceMinimum, ProposedAction.ConfidenceMinimum);
        Assert.Equal(ExtractionOutputValidator.ConfidenceMaximum, ProposedAction.ConfidenceMaximum);
    }

    [Fact]
    public void A_proposal_the_validator_accepts_at_its_limit_is_one_the_aggregate_accepts()
    {
        // The boundary is where a one-off between the two copies would show, and only there.
        ExtractionRun run = ExtractionRun.Start(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            new string('a', 64),
            Guid.CreateVersion7(),
            new ExtractionRunMetadata("Fake", "fixture-catalog", "v1", "1", DateTimeOffset.UnixEpoch, 1, 0, 0),
            ExtractionOutcome.Succeeded,
            failureReason: null,
            warnings: null);

        run.AddProposals(
            [
                new(
                    new string('d', ExtractionOutputValidator.DescriptionMaxLength),
                    new string('o', ExtractionOutputValidator.SuggestedOwnerMaxLength),
                    null,
                    ExtractionOutputValidator.ConfidenceMaximum,
                    new string('e', ExtractionOutputValidator.SourceExcerptMaxLength)),
            ],
            DateTimeOffset.UnixEpoch);

        ProposedAction proposal = Assert.Single(run.Proposals);

        Assert.Equal(ExtractionOutputValidator.DescriptionMaxLength, proposal.Description.Length);
        Assert.Equal(ExtractionOutputValidator.SuggestedOwnerMaxLength, proposal.SuggestedOwner.Length);
        Assert.Equal(ExtractionOutputValidator.SourceExcerptMaxLength, proposal.SourceExcerpt.Length);
    }
}
