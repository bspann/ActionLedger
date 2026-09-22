namespace ActionLedger.Application.Ai;

/// <summary>
/// FR-5's post-validation filter, and the only implementation of it (AD-11). The extractor applies
/// it after the validator has passed; Story 6.2's Evaluation Gate applies the same one.
/// </summary>
/// <remarks>
/// <para>
/// Verification is a filter, never a failure. A proposal whose excerpt cannot be found in the notes
/// is dropped with a recorded warning and the run still succeeds, because one hallucinated citation
/// is not a reason to throw away the commitments the model got right.
/// </para>
/// <para>
/// Both sides go through <see cref="TextNormalization.Normalize"/> before the comparison, which is
/// what FR-5 and FR-38 both say. The catalog's excerpts are already exact ordinal substrings of
/// their notes, which is strictly stronger, so every fixture verifies either way; normalizing is
/// what lets a real model's minor re-punctuation or a rejoined hard wrap survive instead of
/// silently costing recall.
/// </para>
/// </remarks>
public static class ExcerptVerifier
{
    /// <summary>
    /// Whether <paramref name="excerpt"/> occurs in <paramref name="notes"/> once both are
    /// normalized. An excerpt that normalizes to nothing is not verified — the empty string is a
    /// substring of everything, and treating it as a citation would verify a proposal that cites
    /// nothing at all.
    /// </summary>
    /// <param name="excerpt">The sentence the proposal claims to quote.</param>
    /// <param name="notes">The notes the run read, byte-for-byte as they were saved.</param>
    public static bool IsSubstring(string? excerpt, string? notes)
    {
        string normalizedExcerpt = TextNormalization.Normalize(excerpt);

        if (normalizedExcerpt.Length == 0)
        {
            return false;
        }

        return TextNormalization.Normalize(notes).Contains(normalizedExcerpt, StringComparison.Ordinal);
    }
}
