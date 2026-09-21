using System.Text.RegularExpressions;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// DESIGN.md — the design delta is tokens, not a palette, so <c>wwwroot/css/tokens.css</c> and
/// <c>wwwroot/index.html</c> are where the whole specification lives. These tests pin every hex,
/// type role, and spacing value to the one in DESIGN.md's front matter, so a token that drifts
/// from the design document fails the build naming the property and both values.
/// </summary>
public sealed class ThemeAndTokenTests
{
    /// <summary>The four semantic families and the quiet neutral container, light scope.</summary>
    public static TheoryData<string, string> LightSemanticTokens => new()
    {
        { "--al-ai-provenance", "#5B3E96" },
        { "--al-ai-provenance-container", "#EADDFF" },
        { "--al-on-ai-provenance-container", "#2A0E5C" },
        { "--al-human-provenance", "#1F5F8B" },
        { "--al-human-provenance-container", "#D6EAF8" },
        { "--al-on-human-provenance-container", "#0B2A40" },
        { "--al-low-confidence", "#8A5000" },
        { "--al-low-confidence-container", "#FFF1DC" },
        { "--al-on-low-confidence-container", "#4A2A00" },
        { "--al-success", "#1B5E20" },
        { "--al-success-container", "#E3F2E5" },
        { "--al-on-success-container", "#0D3A11" },
        { "--al-neutral-container", "#E8EAED" },
        { "--al-on-neutral-container", "#3C4043" },
    };

    /// <summary>The same families, dark scope. DESIGN.md's <c>-dark</c> pairs.</summary>
    public static TheoryData<string, string> DarkSemanticTokens => new()
    {
        { "--al-ai-provenance", "#D0BCFF" },
        { "--al-ai-provenance-container", "#4A2D82" },
        { "--al-on-ai-provenance-container", "#EADDFF" },
        { "--al-human-provenance", "#9CCAF0" },
        { "--al-human-provenance-container", "#154466" },
        { "--al-on-human-provenance-container", "#D6EAF8" },
        { "--al-low-confidence", "#FFB870" },
        { "--al-low-confidence-container", "#4A2E00" },
        { "--al-on-low-confidence-container", "#FFE2C2" },
        { "--al-success", "#8BD48F" },
        { "--al-success-container", "#1E4A22" },
        { "--al-on-success-container", "#D9F2DB" },
        { "--al-neutral-container", "#3C4043" },
        { "--al-on-neutral-container", "#E8EAED" },
    };

    /// <summary>The three base container pairs the brand layer references, light scope.</summary>
    public static TheoryData<string, string> LightBaseTokens => new()
    {
        { "--al-primary-container", "#D3E4FF" },
        { "--al-on-primary-container", "#001C38" },
        { "--al-error-container", "#FFDAD6" },
        { "--al-on-error-container", "#410002" },
        { "--al-surface-container-low", "#F3F5F9" },
        { "--al-on-surface", "#1A1C1E" },
    };

    /// <summary>The same three pairs, dark scope.</summary>
    public static TheoryData<string, string> DarkBaseTokens => new()
    {
        { "--al-primary-container", "#00497D" },
        { "--al-on-primary-container", "#D3E4FF" },
        { "--al-error-container", "#93000A" },
        { "--al-on-error-container", "#FFDAD6" },
        { "--al-surface-container-low", "#1F2124" },
        { "--al-on-surface", "#E2E2E6" },
    };

    [Theory]
    [MemberData(nameof(LightSemanticTokens))]
    [MemberData(nameof(LightBaseTokens))]
    public void The_light_scope_carries_the_design_document_value(string property, string expected) =>
        AssertToken(TokenCss.LightScope, property, expected, "light");

    [Theory]
    [MemberData(nameof(DarkSemanticTokens))]
    [MemberData(nameof(DarkBaseTokens))]
    public void The_dark_scope_carries_the_design_document_value(string property, string expected) =>
        AssertToken(TokenCss.DarkScope, property, expected, "dark");

    [Theory]
    [InlineData("--al-content-max", "1280px")]
    [InlineData("--al-page-gutter", "24px")]
    [InlineData("--al-notes-pane-min", "360px")]
    public void The_named_spacing_roles_carry_the_design_document_value(string property, string expected) =>
        AssertToken(TokenCss.LightScope, property, expected, "light");

    [Fact]
    public void The_confidence_score_role_is_monospace_at_thirteen_pixels()
    {
        string rule = TokenCss.RuleFor(".al-confidence-score");

        Assert.Contains("'Roboto Mono'", rule, StringComparison.Ordinal);
        Assert.Contains("font-size: 13px", rule, StringComparison.Ordinal);
        Assert.Contains("font-weight: 500", rule, StringComparison.Ordinal);
        Assert.Contains("line-height: 1.2", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void The_confidence_score_role_falls_back_to_a_monospace_face_when_roboto_mono_is_missing()
    {
        // Two-decimal scores only line up in a column while the face is monospace. A fallback
        // list ending in a proportional family would silently break that on an offline load.
        string[] fallbacks =
        [
            .. TokenCss.RuleFor(".al-confidence-score")
                .Split('\n')
                .Single(line => line.Contains("font-family", StringComparison.Ordinal))
                .Split(':', 2)[1]
                .Split(',')
                .Select(family => family.Trim().Trim(';').Trim('\'')),
        ];

        Assert.Equal("Roboto Mono", fallbacks[0]);
        Assert.Contains("ui-monospace", fallbacks, StringComparer.Ordinal);
        Assert.Equal("monospace", fallbacks[^1]);
    }

    [Fact]
    public void The_source_excerpt_role_is_roboto_at_fourteen_pixels()
    {
        string rule = TokenCss.RuleFor(".al-source-excerpt");

        Assert.Contains("'Roboto'", rule, StringComparison.Ordinal);
        Assert.Contains("font-size: 14px", rule, StringComparison.Ordinal);
        Assert.Contains("font-weight: 400", rule, StringComparison.Ordinal);
        Assert.Contains("line-height: 1.5", rule, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Roboto")]
    [InlineData("Roboto+Mono")]
    public void The_host_page_requests_the_font_family(string family)
    {
        string index = WebProject.ReadAllText("wwwroot/index.html");

        string[] stylesheets =
        [
            .. Regex.Matches(index, """<link[^>]*rel="stylesheet"[^>]*>""", RegexOptions.IgnoreCase)
                .Select(match => match.Value),
        ];

        Assert.Contains(
            stylesheets,
            link => link.Contains("fonts.googleapis.com", StringComparison.Ordinal)
                && link.Contains($"family={family}", StringComparison.Ordinal));
    }

    [Fact]
    public void The_host_page_loads_mudblazor_and_the_design_tokens()
    {
        string index = WebProject.ReadAllText("wwwroot/index.html");

        Assert.Contains("_content/MudBlazor/MudBlazor.min.css", index, StringComparison.Ordinal);
        Assert.Contains("_content/MudBlazor/MudBlazor.min.js", index, StringComparison.Ordinal);
        Assert.Contains("css/tokens.css", index, StringComparison.Ordinal);
    }

    private static void AssertToken(string scope, string property, string expected, string scopeName)
    {
        Match declaration = Regex.Match(scope, $@"^\s*{Regex.Escape(property)}\s*:\s*([^;]+);", RegexOptions.Multiline);

        Assert.True(
            declaration.Success,
            $"tokens.css declares no {property} in the {scopeName} scope. DESIGN.md gives it as {expected}.");

        string actual = declaration.Groups[1].Value.Trim();

        Assert.True(
            string.Equals(actual, expected, StringComparison.Ordinal),
            $"{property} is {actual} in the {scopeName} scope; DESIGN.md gives {expected}.");
    }
}
