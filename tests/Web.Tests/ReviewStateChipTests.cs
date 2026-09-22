using ActionLedger.Web.Core;
using ActionLedger.Web.Core.Extraction;
using ActionLedger.Web.Shared;
using Bunit;
using MudBlazor.Services;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// UX-DR6 — the Review State chip: the state's name as text, the DESIGN.md color family as a class,
/// and the edit icon on Edited alone. Text is always present, so color is never the only signal.
/// </summary>
public sealed class ReviewStateChipTests : BunitContext
{
    public ReviewStateChipTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(ReviewState.Pending, Voice.Pending, "al-review-state--pending")]
    [InlineData(ReviewState.Approved, Voice.Approved, "al-review-state--approved")]
    [InlineData(ReviewState.Edited, Voice.Edited, "al-review-state--edited")]
    [InlineData(ReviewState.Rejected, Voice.Rejected, "al-review-state--rejected")]
    public void The_chip_reads_the_state_name_and_carries_its_color_class(ReviewState state, string text, string modifier)
    {
        IRenderedComponent<ReviewStateChip> chip = Render<ReviewStateChip>(parameters => parameters
            .Add(component => component.State, state));

        AngleSharp.Dom.IElement root = chip.Find(".mud-chip");

        Assert.Equal(text, root.TextContent.Trim());
        Assert.Contains(ReviewStateChip.BaseClass, root.ClassList);
        Assert.Contains(modifier, root.ClassList);
    }

    [Theory]
    [InlineData(ReviewState.Pending, false)]
    [InlineData(ReviewState.Approved, false)]
    [InlineData(ReviewState.Edited, true)]
    [InlineData(ReviewState.Rejected, false)]
    public void Only_edited_carries_an_icon(ReviewState state, bool hasIcon)
    {
        // EXPERIENCE.md: "Edited carries the edit icon." Approved shares its color, so the icon is
        // what tells the two apart beyond the word.
        IRenderedComponent<ReviewStateChip> chip = Render<ReviewStateChip>(parameters => parameters
            .Add(component => component.State, state));

        Assert.Equal(hasIcon, chip.FindAll("svg").Count > 0);
    }

    [Fact]
    public void Every_modifier_the_chip_emits_has_a_rule_in_app_css()
    {
        // The class names are the contract between the chip and app.css, and a renamed class would
        // leave every chip on MudBlazor's default grey with nothing else failing.
        string css = WebProject.ReadAllText("wwwroot/css/app.css");

        foreach (ReviewState state in Enum.GetValues<ReviewState>())
        {
            Assert.Contains("." + ReviewStateChip.ModifierFor(state), css, StringComparison.Ordinal);
        }
    }
}
