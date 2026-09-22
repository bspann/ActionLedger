namespace ActionLedger.Application.Ai;

/// <summary>
/// Where a proposal's Source Excerpt sits in the raw notes, for the Review Screen's highlight
/// (FR-10, UX-DR8). The comparison is <see cref="ExcerptVerifier"/>'s, so an excerpt that verified
/// is one this finds.
/// </summary>
/// <remarks>
/// <para>
/// The match is made on normalized text and mapped back to raw offsets through
/// <see cref="TextNormalization"/>'s own map, so the web app never locates or normalizes anything:
/// a second normalizer in the browser is exactly what Rule 5 and AD-11 forbid (AD-15).
/// </para>
/// <para>
/// The span runs from the raw index of the first matched character to one past the raw index of
/// the last. Punctuation the normalizer dropped at either end of the match is therefore outside the
/// span, and punctuation or whitespace between two matched characters is inside it.
/// </para>
/// </remarks>
public static class ExcerptLocator
{
    /// <summary>
    /// The first occurrence of <paramref name="excerpt"/> in <paramref name="notes"/> once both are
    /// normalized, as UTF-16 offsets into the raw <paramref name="notes"/>; or <c>null</c> when the
    /// excerpt normalizes to nothing or is not there.
    /// </summary>
    /// <param name="excerpt">The sentence the proposal quotes.</param>
    /// <param name="notes">The notes the run read, byte-for-byte as they were saved.</param>
    public static ExcerptSpan? Locate(string? excerpt, string? notes)
    {
        string normalizedExcerpt = TextNormalization.Normalize(excerpt);

        if (normalizedExcerpt.Length == 0)
        {
            return null;
        }

        string normalizedNotes = TextNormalization.Normalize(notes, out IReadOnlyList<int> rawIndices);

        int match = normalizedNotes.IndexOf(normalizedExcerpt, StringComparison.Ordinal);

        if (match < 0)
        {
            return null;
        }

        int start = rawIndices[match];
        int end = rawIndices[match + normalizedExcerpt.Length - 1] + 1;

        return new ExcerptSpan(start, end - start);
    }
}

/// <summary>A run of UTF-16 code units in the raw notes.</summary>
/// <param name="Start">The index of the first code unit.</param>
/// <param name="Length">How many code units, always at least one.</param>
public sealed record ExcerptSpan(int Start, int Length);
