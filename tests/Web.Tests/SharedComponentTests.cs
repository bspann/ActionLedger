using ActionLedger.Web.Core;
using ActionLedger.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// UX-DR19 and UX-DR20 — the notices and the confirm dialog every later screen inherits, so the
/// copy and the interaction are assembled in one place rather than re-typed per feature. All
/// three are presentational, which makes a direct render the whole test.
/// </summary>
public sealed class SharedComponentTests : BunitContext
{
    public SharedComponentTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_not_found_notice_reads_not_found_and_links_to_the_parent_list()
    {
        IRenderedComponent<NotFoundNotice> notice = Render<NotFoundNotice>();

        // The h1 is what App.razor's FocusOnNavigate moves focus to on an unmatched route.
        Assert.Equal(Voice.NotFound, notice.Find("h1").TextContent);

        AngleSharp.Dom.IElement link = notice.Find("a");
        Assert.Equal("/meetings", link.GetAttribute("href"));
        Assert.Equal(Voice.Meetings, link.TextContent);
    }

    [Fact]
    public void The_not_found_notice_can_point_at_another_parent()
    {
        IRenderedComponent<NotFoundNotice> notice = Render<NotFoundNotice>(parameters => parameters
            .Add(component => component.ParentHref, "/actions")
            .Add(component => component.ParentLabel, Voice.Actions));

        Assert.Equal("/actions", notice.Find("a").GetAttribute("href"));
        Assert.Equal(Voice.Actions, notice.Find("a").TextContent);
    }

    [Fact]
    public void The_load_failure_notice_prefixes_the_problem_title()
    {
        IRenderedComponent<LoadFailure> failure = Render<LoadFailure>(parameters => parameters
            .Add(component => component.Title, "The resource was not found."));

        // EXPERIENCE.md writes this with a U+2019 apostrophe; the prefix constant carries it and
        // VoiceAndFormatsTests pins it, so assembling it here is all that is left to check.
        Assert.Contains(
            "Couldn’t load. The resource was not found.",
            failure.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_load_failure_notice_raises_retry_once_per_press()
    {
        int retries = 0;

        IRenderedComponent<LoadFailure> failure = Render<LoadFailure>(parameters => parameters
            .Add(component => component.Title, "The resource was not found.")
            .Add(component => component.OnRetry, () => retries++));

        // Reached by its own id, not by MudBlazor's variant class, which would retarget silently
        // the day the Variant changes.
        AngleSharp.Dom.IElement retry = failure.Find($"#{LoadFailure.RetryId}");
        Assert.Equal(Voice.Retry, retry.TextContent);

        retry.Click();
        retry.Click();

        Assert.Equal(2, retries);
    }

    [Fact]
    public async Task The_confirm_dialog_repeats_the_callers_sentence_and_closes_with_true()
    {
        // It lives in Shared/ and takes its message as a parameter because Epic 3's Reject and
        // Epic 4's Complete and Cancelled are the same dialog. Parameterising it now means those
        // stories add a call, not a second dialog.
        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IDialogReference dialog = await ShowAsync(provider);

        Assert.Contains(Voice.NotesImmutable, provider.Markup, StringComparison.Ordinal);
        Assert.Equal(Voice.SaveNotes, provider.Find($"#{ConfirmDialog.ConfirmId}").TextContent.Trim());
        Assert.Equal(Voice.Cancel, provider.Find($"#{ConfirmDialog.CancelId}").TextContent.Trim());

        provider.Find($"#{ConfirmDialog.ConfirmId}").Click();

        DialogResult result = Assert.IsType<DialogResult>(await dialog.Result);
        Assert.False(result.Canceled);
        Assert.Equal(true, result.Data);
    }

    [Fact]
    public async Task The_confirm_dialogs_cancel_closes_with_no_result()
    {
        IRenderedComponent<MudDialogProvider> provider = Render<MudDialogProvider>();
        IDialogReference dialog = await ShowAsync(provider);

        provider.Find($"#{ConfirmDialog.CancelId}").Click();

        DialogResult result = Assert.IsType<DialogResult>(await dialog.Result);
        Assert.True(result.Canceled);
    }

    private async Task<IDialogReference> ShowAsync(IRenderedComponent<MudDialogProvider> provider)
    {
        IDialogService dialogs = Services.GetRequiredService<IDialogService>();

        IDialogReference dialog = await provider.InvokeAsync(() => dialogs.ShowAsync<ConfirmDialog>(
            Voice.SaveNotes,
            new DialogParameters<ConfirmDialog>
            {
                { component => component.Message, Voice.NotesImmutable },
                { component => component.ConfirmLabel, Voice.SaveNotes },
            }));

        provider.WaitForElement($"#{ConfirmDialog.ConfirmId}");

        return dialog;
    }
}
