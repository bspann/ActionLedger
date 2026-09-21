namespace ActionLedger.Web.Tests;

/// <summary>
/// <c>wwwroot/css/tokens.css</c>, split into the two scopes the token tests assert over: the
/// default one, and the one inside <c>@media (prefers-color-scheme: dark)</c>.
/// </summary>
/// <remarks>
/// Dark mode keys off the OS because MudBlazor gives no class to key off: <c>MudThemeProvider</c>
/// swaps the values of its own <c>--mud-palette-*</c> properties when <c>IsDarkMode</c> flips and
/// adds nothing to the document, so a token file cannot follow it by selector. Both layers
/// therefore ride one media query, and the two scopes are what this type hands the tests.
/// </remarks>
internal static class TokenCss
{
    private const string DarkScopeSelector = "@media (prefers-color-scheme: dark)";

    private static readonly string Css = WebProject.ReadAllText("wwwroot/css/tokens.css").ReplaceLineEndings("\n");

    /// <summary>Everything outside the dark media query.</summary>
    internal static string LightScope => Scopes.Light;

    /// <summary>The body of the dark media query.</summary>
    internal static string DarkScope => Scopes.Dark;

    /// <summary>The body of one rule, by selector. Throws when the selector is gone.</summary>
    internal static string RuleFor(string selector)
    {
        int start = Css.IndexOf(selector, StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException($"tokens.css declares no {selector} rule.");
        }

        return Css.Substring(start, BlockLengthFrom(Css, start));
    }

    /// <summary>
    /// Both scopes are cut in one pass, so neither depends on the other's initialization order.
    /// </summary>
    private static readonly (string Light, string Dark) Scopes = SplitScopes();

    private static (string Light, string Dark) SplitScopes()
    {
        int start = Css.IndexOf(DarkScopeSelector, StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException(
                $"tokens.css declares no \"{DarkScopeSelector}\" scope, so no token has a dark pair.");
        }

        int length = BlockLengthFrom(Css, start);

        return (Css.Remove(start, length), Css.Substring(start, length));
    }

    /// <summary>
    /// From <paramref name="start"/> to the close of the first brace-balanced block after it.
    /// Brace matching rather than a regex, because the dark scope nests a <c>:root</c> block.
    /// </summary>
    private static int BlockLengthFrom(string css, int start)
    {
        int open = css.IndexOf('{', start);

        if (open < 0)
        {
            throw new InvalidOperationException($"No block follows position {start} in tokens.css.");
        }

        int depth = 0;

        for (int index = open; index < css.Length; index++)
        {
            depth += css[index] switch { '{' => 1, '}' => -1, _ => 0 };

            if (depth == 0)
            {
                return index - start + 1;
            }
        }

        throw new InvalidOperationException($"The block opened at position {open} in tokens.css is never closed.");
    }
}
