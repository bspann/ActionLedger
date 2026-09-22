using System.Globalization;

namespace ActionLedger.Application.Ai;

/// <summary>
/// AD-11, FR-5 — layer two of validation: lengths, ranges, blankness and date format, checked by
/// hand.
/// </summary>
/// <remarks>
/// <para>
/// Hand-rolled because <c>JsonSchema.Net</c> is licence-banned (<c>Directory.Packages.props</c>),
/// and because the checks are six lines of arithmetic against a schema this repository owns.
/// </para>
/// <para>
/// Validation is all-or-nothing per response and over-length is a failure, never a truncation
/// (FR-5): a description the model ran past 500 characters is a response that did not follow the
/// schema, and silently cutting it would hand a reviewer half a commitment to approve.
/// </para>
/// <para>
/// The bounds below are the single place they are written in C#. Every other consumer reads them
/// from here rather than redeclaring them, and
/// <c>ExtractionContractTests.The_validator_bounds_are_the_committed_schemas_bounds</c> reads the
/// same four keywords out of <see cref="ExtractionSchema.CommittedJsonText"/> and compares — so
/// editing <c>maxLength</c> in the committed file and not here goes red.
/// </para>
/// </remarks>
public static class ExtractionOutputValidator
{
    /// <summary><c>description.minLength</c> in the committed schema.</summary>
    public const int DescriptionMinLength = 1;

    /// <summary><c>description.maxLength</c> in the committed schema.</summary>
    public const int DescriptionMaxLength = 500;

    /// <summary><c>suggestedOwner.maxLength</c>. There is no minimum: an unstated owner is <c>""</c>.</summary>
    public const int SuggestedOwnerMaxLength = 100;

    /// <summary><c>sourceExcerpt.minLength</c> in the committed schema.</summary>
    public const int SourceExcerptMinLength = 1;

    /// <summary><c>sourceExcerpt.maxLength</c> in the committed schema.</summary>
    public const int SourceExcerptMaxLength = 1000;

    /// <summary><c>confidence.minimum</c> in the committed schema. Inclusive.</summary>
    public const double ConfidenceMinimum = 0;

    /// <summary><c>confidence.maximum</c> in the committed schema. Inclusive.</summary>
    public const double ConfidenceMaximum = 1;

    /// <summary>What <c>suggestedDueDate.format: "date"</c> means here, exactly.</summary>
    public const string DueDateFormat = "yyyy-MM-dd";

    /// <summary>
    /// The first thing wrong with <paramref name="output"/>, or <c>null</c> when nothing is. The
    /// reason names the member and the index of the offending action, because the extractor puts it
    /// straight into <c>ExtractionResult.Failed</c> and the UI shows it to a human verbatim.
    /// </summary>
    /// <param name="output">A response that has already deserialized strictly.</param>
    public static string? Validate(ExtractionOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        for (int index = 0; index < output.Actions.Count; index++)
        {
            string? failure = Validate(output.Actions[index], index);

            if (failure is not null)
            {
                return failure;
            }
        }

        return null;
    }

    private static string? Validate(ExtractedAction? action, int index)
    {
        // `{"actions":[null]}` deserializes: nullable annotations describe the C# type, they do not
        // make System.Text.Json reject a null collection element. Dereferencing it here would throw
        // a NullReferenceException out of a port documented never to throw for a provider failure,
        // skipping the retry and turning a bad response into a 500. It is a validation failure.
        if (action is null)
        {
            return Reason(index, "(the action itself)", "must be an object, was null");
        }

        if (action.Description.Length is < DescriptionMinLength or > DescriptionMaxLength)
        {
            return Reason(
                index,
                "description",
                $"must be {DescriptionMinLength} to {DescriptionMaxLength} characters, was {action.Description.Length}");
        }

        // Blank-after-trim is a validation failure, not a row. `minLength: 1` counts characters, so
        // "   " satisfies the bound above — and `ProposedAction` refuses it, which would throw a
        // DomainRuleException out of `ExtractionRun.AddProposals` and answer the POST with a 409
        // and *no* run row: exactly the outcome AD-11 exists to prevent. Refusing it here sends it
        // down the ordinary failure path instead, so the run persists as Failed and the caller
        // still gets its 201.
        if (string.IsNullOrWhiteSpace(action.Description))
        {
            return Reason(index, "description", "must not be blank");
        }

        if (action.SuggestedOwner.Length > SuggestedOwnerMaxLength)
        {
            return Reason(
                index,
                "suggestedOwner",
                $"must be at most {SuggestedOwnerMaxLength} characters, was {action.SuggestedOwner.Length}");
        }

        if (action.SuggestedDueDate is { } dueDate
            && !DateOnly.TryParseExact(dueDate, DueDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return Reason(index, "suggestedDueDate", $"must be null or a {DueDateFormat} date, was '{dueDate}'");
        }

        if (double.IsNaN(action.Confidence)
            || action.Confidence < ConfidenceMinimum
            || action.Confidence > ConfidenceMaximum)
        {
            return Reason(
                index,
                "confidence",
                $"must be between {ConfidenceMinimum} and {ConfidenceMaximum} inclusive, was {action.Confidence.ToString(CultureInfo.InvariantCulture)}");
        }

        if (action.SourceExcerpt.Length is < SourceExcerptMinLength or > SourceExcerptMaxLength)
        {
            return Reason(
                index,
                "sourceExcerpt",
                $"must be {SourceExcerptMinLength} to {SourceExcerptMaxLength} characters, was {action.SourceExcerpt.Length}");
        }

        // Same reason as the description: `ProposedAction` refuses a blank excerpt, and a refusal
        // reaching the aggregate is a 409 with nothing persisted.
        if (string.IsNullOrWhiteSpace(action.SourceExcerpt))
        {
            return Reason(index, "sourceExcerpt", "must not be blank");
        }

        return null;
    }

    private static string Reason(int index, string member, string problem) =>
        $"Validation failed: actions[{index}].{member} {problem}.";
}
