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
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder normalized = new(text.Length);
        bool pendingSpace = false;

        foreach (char character in text.ToLowerInvariant())
        {
            if (char.IsPunctuation(character) || char.IsSymbol(character))
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                // A run of whitespace of any length — including the newline of a hard wrap — becomes
                // one space, and only once something follows it, so there is nothing to trim at the
                // end and no leading space to trim at the start.
                pendingSpace = normalized.Length > 0;

                continue;
            }

            if (pendingSpace)
            {
                normalized.Append(' ');
                pendingSpace = false;
            }

            normalized.Append(character);
        }

        return normalized.ToString();
    }
}
