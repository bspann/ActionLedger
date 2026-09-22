using System.Text;

namespace ActionLedger.Application.Ai;

/// <summary>
/// FR-38's text normalization, and the only implementation of it in the solution (AD-11).
/// </summary>
/// <remarks>
/// <para>
/// <c>DependencyRuleTests</c> Rule 5 fails the build on any type named <c>*Normaliz*</c> outside
/// this namespace, so a second normalizer cannot be added quietly — which matters because the
/// Fake provider's answer key, <see cref="ExcerptVerifier"/>, and Story 6.2's scorer all have to
/// agree on what "the same text" means.
/// </para>
/// <para>
/// The rule is FR-38's literally: punctuation and symbols are <em>dropped</em>, not turned into
/// separators. So <c>"P. Ram"</c> keeps its two tokens — the full stop goes and the space after it
/// survives, giving <c>"p ram"</c> — while <c>"end-of-month"</c>, whose hyphens have no space
/// beside them, closes up to the single token <c>"endofmonth"</c>. That second outcome is the rule
/// working, not a flaw in it: both sides of every comparison go through this method, so a hyphen a
/// model adds or removes cannot change the answer.
/// </para>
/// <para>
/// Ordering follows from that. Whitespace is collapsed in the same pass that drops punctuation, so
/// a run of whitespace of any length — including the newline of a hard wrap — becomes one space,
/// and no leading or trailing space is ever emitted.
/// </para>
/// </remarks>
public static class TextNormalization
{
    /// <summary>
    /// Lowercases invariantly, drops every punctuation and symbol character, collapses every run of
    /// whitespace to a single space, and trims. Never returns <c>null</c>.
    /// </summary>
    /// <param name="text">Any text; <c>null</c> normalizes to the empty string.</param>
    public static string Normalize(string? text) => Normalize(text, out _);

    /// <summary>
    /// <see cref="Normalize(string?)"/>, also answering where each normalized character came from:
    /// <paramref name="rawIndices"/>[i] is the index in <paramref name="text"/> of normalized
    /// character i. <see cref="ExcerptLocator"/> maps a normalized match back to the raw notes
    /// through it, so the one normalizer is also the one that knows the offsets (AD-11).
    /// </summary>
    /// <remarks>
    /// <see cref="string.ToLowerInvariant"/> keeps the UTF-16 length, so an index into the
    /// lowercased text is an index into the raw text. A collapsed space maps to the first
    /// whitespace character of the run it replaced.
    /// </remarks>
    /// <param name="text">Any text; <c>null</c> normalizes to the empty string.</param>
    /// <param name="rawIndices">One raw index per character of the result, in order.</param>
    internal static string Normalize(string? text, out IReadOnlyList<int> rawIndices)
    {
        if (string.IsNullOrEmpty(text))
        {
            rawIndices = [];

            return string.Empty;
        }

        StringBuilder normalized = new(text.Length);
        List<int> map = new(text.Length);
        bool pendingSpace = false;
        int spaceIndex = 0;
        string lowered = text.ToLowerInvariant();

        for (int index = 0; index < lowered.Length; index++)
        {
            char character = lowered[index];

            if (char.IsPunctuation(character) || char.IsSymbol(character))
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!pendingSpace)
                {
                    spaceIndex = index;
                }

                // A run of whitespace of any length — including the newline of a hard wrap — becomes
                // one space, and only once something follows it, so there is nothing to trim at the
                // end and no leading space to trim at the start.
                pendingSpace = normalized.Length > 0;

                continue;
            }

            if (pendingSpace)
            {
                normalized.Append(' ');
                map.Add(spaceIndex);
                pendingSpace = false;
            }

            normalized.Append(character);
            map.Add(index);
        }

        rawIndices = map;

        return normalized.ToString();
    }
}
