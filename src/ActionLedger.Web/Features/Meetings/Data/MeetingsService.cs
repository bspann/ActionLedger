using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Errors;

namespace ActionLedger.Web.Features.Meetings.Data;

/// <summary>
/// AD-14 — the Meetings feature's HTTP seam, built on the <c>AuthService</c> exemplar. It is the
/// only file in the feature that may name an <c>ActionLedger.Web.Core.Api</c> type, so the two
/// pages, the dialog, and their tests see only the records below and <see cref="ApiFailure"/>.
/// </summary>
/// <remarks>
/// <para>
/// It is also where the calendar date is converted. The contract writes <c>meetingDate</c> as a
/// <c>date</c>, but the generator maps it to <c>DateTimeOffset</c> carrying a
/// <c>DateFormatConverter</c>. Converting anywhere else would mean a page holding an instant for
/// something that has no time of day, and <c>Formats.Date</c> takes a <see cref="DateOnly"/>.
/// </para>
/// </remarks>
public sealed class MeetingsService(IActionLedgerApiClient client)
{
    /// <summary>
    /// The contract's notes limit (<c>SaveMeetingNotesCommand.Text</c> is
    /// <c>StringLength(50000, MinimumLength = 1)</c>). The paste area counts against it, and it is
    /// declared at the seam because the contract is what decides it.
    /// </summary>
    public const int NotesMaxLength = 50_000;

    /// <summary>The contract's Title limit (<c>CreateMeetingCommand.Title</c>).</summary>
    public const int TitleMaxLength = 200;

    /// <summary>
    /// The per-attendee limit. EXPERIENCE.md writes it as "1 to 100 characters" and Story 2.1's
    /// matrix refuses a 101-character attendee with a 400.
    /// </summary>
    public const int AttendeeMaxLength = 100;

    public Task<MeetingOutcome<MeetingPage>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        CallAsync(
            async token =>
            {
                PagedResultOfMeetingSummaryDto result = await client
                    .ListMeetingsAsync(page, pageSize, token)
                    .ConfigureAwait(false);

                // The server's order is the order. UX-DR12 forbids a client-side re-sort: the
                // paginator only ever holds one page, so sorting it would shuffle 50 rows against
                // a total the server counted across all of them.
                return new MeetingPage(
                    [.. result.Items.Select(ToListItem)],
                    result.Page,
                    result.PageSize,
                    result.Total);
            },
            cancellationToken);

    public Task<MeetingOutcome<Guid>> CreateAsync(
        string title,
        DateOnly date,
        IReadOnlyList<string> attendees,
        CancellationToken cancellationToken = default) =>
        CallAsync(
            async token =>
            {
                MeetingCreatedDto created = await client
                    .CreateMeetingAsync(
                        new CreateMeetingCommand
                        {
                            Title = title,
                            MeetingDate = ToMeetingDate(date),
                            Attendees = [.. attendees],
                        },
                        token)
                    .ConfigureAwait(false);

                return created.Id;
            },
            cancellationToken);

    public Task<MeetingOutcome<MeetingDetail>> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        CallAsync(
            async token =>
            {
                MeetingDetailDto detail = await client.GetMeetingAsync(id, token).ConfigureAwait(false);

                return new MeetingDetail(
                    detail.Id,
                    detail.Title,
                    ToDate(detail.MeetingDate),
                    [.. detail.Attendees],
                    detail.CreatedAt,
                    detail.Notes is null ? null : ToNotes(detail.Notes));
            },
            cancellationToken);

    public Task<MeetingOutcome<MeetingNotes>> SaveNotesAsync(
        Guid id,
        string text,
        CancellationToken cancellationToken = default) =>
        CallAsync(
            async token =>
            {
                // ADR-002 — the text goes over the wire exactly as it was typed. No Trim, no
                // newline normalization: the server stores it byte-for-byte and hashes what it
                // stored, so anything tidied here would silently become the record.
                MeetingNotesDto saved = await client
                    .SaveMeetingNotesAsync(id, new SaveMeetingNotesCommand { Text = text }, token)
                    .ConfigureAwait(false);

                return ToNotes(saved);
            },
            cancellationToken);

    /// <summary>
    /// The four catch arms <c>AuthService</c> documents, written once because this seam makes four
    /// calls rather than one. Every generated exception shape stops here and leaves as an
    /// <see cref="ApiFailure"/>; a cancellation the caller asked for keeps propagating, because
    /// that is a decision to stop rather than a failure to render.
    /// </summary>
    private static async Task<MeetingOutcome<T>> CallAsync<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        try
        {
            return MeetingOutcome<T>.Succeeded(await call(cancellationToken).ConfigureAwait(false));
        }
        catch (ApiException exception)
        {
            // ApiException<ProblemDetails>, ApiException<ValidationProblemDetails>, and the bare
            // ApiException an undeclared status arrives as. ApiFailures.From tells them apart.
            return MeetingOutcome<T>.Failed(ApiFailures.From(exception));
        }
        catch (HttpRequestException exception)
        {
            // A transport failure never becomes an ApiException, so it needs its own arm.
            return MeetingOutcome<T>.Failed(ApiFailures.From(exception));
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A timed-out request throws TaskCanceledException, which is neither of the above.
            return MeetingOutcome<T>.Failed(ApiFailures.From(exception));
        }
    }

    private static MeetingListItem ToListItem(MeetingSummaryDto summary) =>
        new(
            summary.Id,
            summary.Title,
            ToDate(summary.MeetingDate),
            summary.RunCount,
            summary.TrackedActionCount);

    private static MeetingNotes ToNotes(MeetingNotesDto notes) =>
        new(notes.Id, notes.Text, notes.Sha256, notes.SavedAt);

    /// <summary>Inbound: the instant the converter produced, back to the calendar date it wrote.</summary>
    private static DateOnly ToDate(DateTimeOffset value) => DateOnly.FromDateTime(value.Date);

    /// <summary>
    /// Outbound: midnight at zero offset. <c>DateFormatConverter</c> writes only the
    /// <c>yyyy-MM-dd</c> part, so the time and the offset never reach the wire — but picking them
    /// deliberately is what stops a local-time default from moving the date across a day boundary.
    /// </summary>
    private static DateTimeOffset ToMeetingDate(DateOnly value) =>
        new(value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}

/// <summary>
/// AD-14 — one row of the Meeting table, in web-owned types. <c>MeetingSummaryDto</c> is an
/// <c>ActionLedger.Web.Core.Api</c> type and <c>Features.Meetings</c>, where the pages live, is
/// outside the seam allowlist <c>WebStructureTests</c> enforces.
/// </summary>
/// <param name="Date">
/// The calendar date, not an instant. The generated DTO carries a <c>DateTimeOffset</c> because
/// that is what the generator maps <c>format: date</c> to; <c>MeetingsService</c> converts it.
/// </param>
/// <param name="RunCount">Zero until Story 2.5 persists a run. The web never computes it.</param>
/// <param name="TrackedActionCount">Zero until Story 3.1. The web never computes it either.</param>
public sealed record MeetingListItem(
    Guid Id,
    string Title,
    DateOnly Date,
    int RunCount,
    int TrackedActionCount);

/// <summary>
/// One page of the Meeting list. <paramref name="Total"/> is counted across every page, which is
/// what lets <c>MudTable.ServerData</c> and the API agree on how many pages there are.
/// </summary>
public sealed record MeetingPage(
    IReadOnlyList<MeetingListItem> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>
/// ADR-002 — the notes, once they exist. There is no web-owned "draft notes" counterpart: notes
/// attach exactly once, and until they do the detail page holds a plain string.
/// </summary>
public sealed record MeetingNotes(Guid Id, string Text, string Sha256, DateTimeOffset SavedAt);

/// <summary>
/// AD-14 — one Meeting, as the detail page sees it. <paramref name="Notes"/> being <c>null</c> is
/// the whole immutability branch: the page renders the paste area or the read-only text from it,
/// and never a flag that a later story could invert.
/// </summary>
public sealed record MeetingDetail(
    Guid Id,
    string Title,
    DateOnly Date,
    IReadOnlyList<string> Attendees,
    DateTimeOffset CreatedAt,
    MeetingNotes? Notes);

/// <summary>
/// AD-14 — what every <see cref="MeetingsService"/> call returns, mirroring <c>SignInOutcome</c>.
/// Generic because this seam has four calls rather than one, and each carries a different value.
/// </summary>
/// <remarks>
/// <para>
/// It lives beside the service rather than in <c>Core/</c>, which is where <c>SignInOutcome</c>
/// sits, because nothing outside the Meetings feature consumes it — <c>SessionState</c> is what
/// pulled the sign-in outcome up a level.
/// </para>
/// <para>
/// Callers branch on <see cref="Failure"/>, never on <see cref="Value"/>. <c>T</c> is
/// unconstrained, so for <c>MeetingOutcome&lt;Guid&gt;</c> the nullable annotation is only an
/// annotation: a failed create still carries <c>Guid.Empty</c>, and <c>Value is { }</c> would be
/// true for it.
/// </para>
/// </remarks>
public sealed record MeetingOutcome<T>
{
    private MeetingOutcome(T? value, ApiFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    /// <summary>The result, or <c>null</c> when the call did not succeed.</summary>
    public T? Value { get; }

    /// <summary>The failure, or <c>null</c> when the call succeeded.</summary>
    public ApiFailure? Failure { get; }

    public static MeetingOutcome<T> Succeeded(T value) => new(value, null);

    public static MeetingOutcome<T> Failed(ApiFailure failure) => new(default, failure);
}
