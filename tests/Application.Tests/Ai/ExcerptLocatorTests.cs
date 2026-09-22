using ActionLedger.Application.Ai;
using Xunit;

namespace ActionLedger.Application.Tests.Ai;

/// <summary>
/// The Review Screen's highlight span: <see cref="ExcerptVerifier"/>'s normalized match, mapped back
/// to UTF-16 offsets in the raw notes through <see cref="TextNormalization"/>'s own map (AD-11).
/// </summary>
public sealed class ExcerptLocatorTests
{
    [Fact]
    public void An_exact_excerpt_spans_its_text_without_the_punctuation_normalization_dropped()
    {
        const string notes = "A.\nDana will call Bob.";

        ExcerptSpan span = Assert.IsType<ExcerptSpan>(ExcerptLocator.Locate("Dana will call Bob.", notes));

        Assert.Equal(new ExcerptSpan(3, 18), span);
        Assert.Equal("Dana will call Bob", notes.Substring(span.Start, span.Length));
    }

    [Fact]
    public void A_normalized_only_match_spans_the_raw_text_including_collapsed_whitespace()
    {
        const string notes = "Dana  will call\r\nBob!";

        ExcerptSpan span = Assert.IsType<ExcerptSpan>(ExcerptLocator.Locate("dana will call bob", notes));

        Assert.Equal("Dana  will call\r\nBob", notes.Substring(span.Start, span.Length));
    }

    [Fact]
    public void Punctuation_inside_the_match_is_inside_the_span()
    {
        const string notes = "Notes: P. Ram to arrange the pickup; target the 10th.";

        ExcerptSpan span = Assert.IsType<ExcerptSpan>(ExcerptLocator.Locate("P Ram to arrange the pickup target the 10th", notes));

        Assert.Equal("P. Ram to arrange the pickup; target the 10th", notes.Substring(span.Start, span.Length));
    }

    [Theory]
    [InlineData("Réunion à Zürich.\nDana will call Bob.", "Dana will call Bob")]
    [InlineData("Kickoff \U0001F680 done.\nDana will call Bob.", "Dana will call Bob")]
    [InlineData("\U0001F4CB Notes \u2014 José Müller will call Bob!", "José Müller will call Bob")]
    public void Non_ascii_text_before_or_in_the_excerpt_keeps_the_span_on_the_raw_substring(string notes, string expected)
    {
        ExcerptSpan span = Assert.IsType<ExcerptSpan>(ExcerptLocator.Locate(expected + ".", notes));

        Assert.Equal(notes.IndexOf(expected, StringComparison.Ordinal), span.Start);
        Assert.Equal(expected, notes.Substring(span.Start, span.Length));
    }

    [Fact]
    public void The_first_occurrence_wins()
    {
        const string notes = "Dana will call Bob. Later: Dana will call Bob.";

        Assert.Equal(new ExcerptSpan(0, 18), ExcerptLocator.Locate("Dana will call Bob", notes));
    }

    [Theory]
    [InlineData("Nobody said this.")]
    [InlineData("Dana will call Bobby")]
    public void An_excerpt_that_is_not_in_the_notes_has_no_span(string excerpt)
    {
        Assert.Null(ExcerptLocator.Locate(excerpt, "A.\nDana will call Bob."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...!?")]
    public void An_excerpt_that_normalizes_to_nothing_has_no_span(string? excerpt)
    {
        Assert.Null(ExcerptLocator.Locate(excerpt, "A.\nDana will call Bob."));
    }

    [Fact]
    public void Missing_notes_have_no_span()
    {
        Assert.Null(ExcerptLocator.Locate("Dana will call Bob.", null));
    }

    [Theory]
    [InlineData("Dana will call Bob.", "A.\nDana will call Bob.")]
    [InlineData("dana will call bob", "Dana  will call\r\nBob!")]
    [InlineData("Nobody said this.", "A.\nDana will call Bob.")]
    public void It_agrees_with_the_verifier(string excerpt, string notes)
    {
        Assert.Equal(ExcerptVerifier.IsSubstring(excerpt, notes), ExcerptLocator.Locate(excerpt, notes) is not null);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  Dana  WILL call\r\n\r\nBob!  ", "dana will call bob")]
    [InlineData("P. Ram", "p ram")]
    [InlineData("end-of-month", "endofmonth")]
    [InlineData("a . b", "a b")]
    public void Normalize_output_is_unchanged_by_the_mapped_overload(string? text, string expected)
    {
        Assert.Equal(expected, TextNormalization.Normalize(text));
    }
}
