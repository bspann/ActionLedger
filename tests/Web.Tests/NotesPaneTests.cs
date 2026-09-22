using ActionLedger.Web.Features.Review;
using ActionLedger.Web.Features.Review.Data;
using Bunit;
using MudBlazor.Services;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// UX-DR8 — the notes pane slices where the server said and nowhere else. A range that does not
/// fit the text means the two reads disagree, and the safe answer is no mark at all.
/// </summary>
public sealed class NotesPaneTests : BunitContext
{
    private const string Notes = "A.\nDana will call Bob.";

    public NotesPaneTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(3, 20)]
    [InlineData(20, 5)]
    [InlineData(-1, 4)]
    [InlineData(3, 0)]
    [InlineData(3, -2)]
    public void A_range_that_does_not_fit_renders_no_mark_and_the_notes_verbatim(int start, int length)
    {
        IRenderedComponent<NotesPane> pane = RenderPane(new ExcerptRange(start, length));

        Assert.Empty(pane.FindAll("mark"));
        Assert.Equal(Notes, pane.Find(".al-notes").TextContent);
    }

    [Fact]
    public void A_range_ending_exactly_at_the_end_renders_the_mark()
    {
        IRenderedComponent<NotesPane> pane = RenderPane(new ExcerptRange(3, Notes.Length - 3));

        Assert.Equal("Dana will call Bob.", pane.Find($"mark#{NotesPane.HighlightId}").TextContent);
        Assert.Equal(Notes, pane.Find(".al-notes").TextContent);
    }

    [Fact]
    public void No_range_renders_no_mark()
    {
        IRenderedComponent<NotesPane> pane = RenderPane(null);

        Assert.Empty(pane.FindAll("mark"));
        Assert.Equal(Notes, pane.Find(".al-notes").TextContent);
    }

    private IRenderedComponent<NotesPane> RenderPane(ExcerptRange? range) =>
        Render<NotesPane>(parameters => parameters
            .Add(component => component.Text, Notes)
            .Add(component => component.Highlight, range));
}
