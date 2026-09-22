using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ActionLedger.Api.Errors;
using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Meetings;
using ActionLedger.Domain.Meetings;
using ActionLedger.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// The success half of the story's I/O matrix, asserted over the real HTTP pipeline: the 201 and
/// the <c>Location</c> it points at, the 200 a saved note answers with, and the detail read.
/// </summary>
/// <remarks>
/// <para>
/// <c>MeetingsEndpointTests</c> covers what model validation refuses before a handler runs, and
/// <c>Infrastructure.Tests</c> covers what the database decides. Neither can see the status code a
/// *successful* write leaves with, because <c>TestApi</c>'s connection string points at nothing —
/// so the three persistence ports are replaced here with in-memory fakes and everything above
/// them, including <c>ICurrentUser</c>, stays exactly what the host wires up.
/// </para>
/// <para>
/// That is what makes the <c>createdByUserId</c> assertion meaningful: the value is read from the
/// claims principal of a token this test minted, through the real
/// <c>ClaimsPrincipalCurrentUser</c>, and nothing in the request body could have supplied it.
/// </para>
/// <para>
/// <c>CreatedAtAction</c> is the specific reason this file exists. It resolves the detail route by
/// action name at request time, so a renamed action or a changed route template turns the 201 into
/// a 500 that no contract test and no unit test would see.
/// </para>
/// </remarks>
public sealed class MeetingsSuccessResponseTests
{
    private const string Meetings = "/api/v1/meetings";

    [Fact]
    public async Task Creating_a_meeting_is_a_201_whose_location_is_the_detail_route_that_serves_it()
    {
        MeetingStore store = new();
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        HttpResponseMessage response = await client.PostAsJsonAsync(
            Meetings,
            new { title = "Pinecrest weekly sync", meetingDate = "2026-09-21", attendees = new[] { "Dana Whitfield" } },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        JsonElement created = await ReadAsync(response);
        Guid id = created.GetProperty("id").GetGuid();

        Assert.NotEqual(Guid.Empty, id);

        // The Location has to be usable, not merely present: CreatedAtAction builds it from the
        // action name, so this is what proves the name it was given still resolves.
        Assert.NotNull(response.Headers.Location);
        Assert.Equal($"{Meetings}/{id}", response.Headers.Location!.AbsolutePath);

        HttpResponseMessage followed = await client.GetAsync(response.Headers.Location, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
        Assert.Equal(id, (await ReadAsync(followed)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task The_created_meeting_is_attributed_to_the_token_that_sent_it()
    {
        MeetingStore store = new();
        await using TestApi api = new() { ReplaceServices = store.Replace };

        Guid actor = Guid.CreateVersion7();

        using HttpClient client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new(
            "Bearer",
            TestApi.TokenFor(nameof(Role.ActionOfficer), actor.ToString()));

        HttpResponseMessage response = await client.PostAsJsonAsync(
            Meetings,
            // A body that tries to name a different author. The property is not on the command, so
            // it binds to nothing — and the answer is still the token's subject.
            new { title = "Budget review", meetingDate = "2026-09-20", attendees = Array.Empty<string>(), createdByUserId = Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(actor, (await ReadAsync(response)).GetProperty("createdByUserId").GetGuid());
    }

    [Fact]
    public async Task Saving_notes_is_a_200_carrying_the_hash_of_exactly_what_was_sent()
    {
        MeetingStore store = new();
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Meetings,
            new { title = "Equipment inventory", meetingDate = "2026-09-19", attendees = Array.Empty<string>() },
            TestContext.Current.CancellationToken);

        Guid id = (await ReadAsync(created)).GetProperty("id").GetGuid();

        const string Pasted = "  Dana will order the replacement scanners.\r\n";

        HttpResponseMessage saved = await client.PutAsJsonAsync(
            $"{Meetings}/{id}/notes",
            new { text = Pasted },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        JsonElement notes = await ReadAsync(saved);

        Assert.Equal(Pasted, notes.GetProperty("text").GetString());
        Assert.Equal(MeetingNotes.Sha256Length, notes.GetProperty("sha256").GetString()!.Length);

        // The detail read gives back the same bytes, including the leading spaces and the CRLF.
        HttpResponseMessage detail = await client.GetAsync($"{Meetings}/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(Pasted, (await ReadAsync(detail)).GetProperty("notes").GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_second_save_is_a_409_conflict_and_the_detail_read_still_carries_the_first_text()
    {
        MeetingStore store = new();
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        HttpResponseMessage created = await client.PostAsJsonAsync(
            Meetings,
            new { title = "Range safety brief", meetingDate = "2026-09-18", attendees = Array.Empty<string>() },
            TestContext.Current.CancellationToken);

        Guid id = (await ReadAsync(created)).GetProperty("id").GetGuid();

        const string First = "Priya will book the range for Thursday.";

        HttpResponseMessage saved = await client.PutAsJsonAsync(
            $"{Meetings}/{id}/notes",
            new { text = First },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        // The acceptance criterion is about the HTTP surface, not about the aggregate: the rule is
        // proven at the handler and again against a real unique index, but neither of those can
        // say what status or what `type` slug a caller is actually handed.
        HttpResponseMessage second = await client.PutAsJsonAsync(
            $"{Meetings}/{id}/notes",
            new { text = "A replacement nobody gets to make." },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);

        JsonElement problem = await ReadAsync(second);

        Assert.Equal(ProblemTypes.Conflict, problem.GetProperty("type").GetString());
        Assert.Equal(StatusCodes.Status409Conflict, problem.GetProperty("status").GetInt32());

        // And the refused write changed nothing a reader can see.
        HttpResponseMessage detail = await client.GetAsync($"{Meetings}/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(First, (await ReadAsync(detail)).GetProperty("notes").GetProperty("text").GetString());
    }

    [Fact]
    public async Task The_list_pages_with_the_window_the_query_string_asked_for()
    {
        MeetingStore store = new();
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        // Two meetings on different days, created oldest-first, so "newest meeting first" and
        // "insertion order" disagree and the assertion below can tell them apart.
        foreach ((string Title, string Held) meeting in new[]
        {
            (Title: "Older standup", Held: "2026-09-10"),
            (Title: "Newer standup", Held: "2026-09-17"),
        })
        {
            HttpResponseMessage created = await client.PostAsJsonAsync(
                Meetings,
                new { title = meeting.Title, meetingDate = meeting.Held, attendees = Array.Empty<string>() },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        // A window wide enough for both: the envelope, the order, and the published counts.
        HttpResponseMessage all = await client.GetAsync(
            $"{Meetings}?page=1&pageSize=50",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, all.StatusCode);

        JsonElement page = await ReadAsync(all);

        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(50, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, page.GetProperty("total").GetInt32());

        JsonElement[] items = [.. page.GetProperty("items").EnumerateArray()];

        Assert.Equal(2, items.Length);
        Assert.Equal("Newer standup", items[0].GetProperty("title").GetString());
        Assert.Equal("Older standup", items[1].GetProperty("title").GetString());
        // Nothing has been run against either meeting, so the correlated count is the real 0.
        // RunsEndpointTests is where a meeting that *has* runs reports them.
        Assert.Equal(0, items[0].GetProperty("runCount").GetInt32());
        Assert.Equal(0, items[0].GetProperty("trackedActionCount").GetInt32());

        // And a one-row window on the second page. This is what distinguishes page from pageSize:
        // swapping the two arguments the controller forwards serves a different row here.
        HttpResponseMessage second = await client.GetAsync(
            $"{Meetings}?page=2&pageSize=1",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        JsonElement windowed = await ReadAsync(second);

        Assert.Equal(2, windowed.GetProperty("page").GetInt32());
        Assert.Equal(1, windowed.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, windowed.GetProperty("total").GetInt32());
        Assert.Equal(
            "Older standup",
            Assert.Single(windowed.GetProperty("items").EnumerateArray()).GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_create_that_omits_attendees_entirely_round_trips_as_an_empty_array()
    {
        MeetingStore store = new();
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        // The matrix's "omitted" case: the property is absent from the JSON, not null and not [].
        HttpResponseMessage created = await client.PostAsJsonAsync(
            Meetings,
            new { title = "Quiet meeting", meetingDate = "2026-09-16" },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        Guid id = (await ReadAsync(created)).GetProperty("id").GetGuid();

        HttpResponseMessage detail = await client.GetAsync($"{Meetings}/{id}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Empty((await ReadAsync(detail)).GetProperty("attendees").EnumerateArray());
    }

    [Fact]
    public async Task Reading_a_meeting_that_does_not_exist_is_a_404_not_found()
    {
        MeetingStore store = new();
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        HttpResponseMessage response = await client.GetAsync(
            $"{Meetings}/{Guid.CreateVersion7()}",
            TestContext.Current.CancellationToken);

        await AssertNotFound(response);
    }

    [Fact]
    public async Task Saving_notes_on_a_meeting_that_does_not_exist_is_a_404_not_found()
    {
        MeetingStore store = new();
        await using TestApi api = new() { ReplaceServices = store.Replace };
        using HttpClient client = api.CreateClientAs(nameof(Role.ActionOfficer));

        HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{Meetings}/{Guid.CreateVersion7()}/notes",
            new { text = "Notes for a meeting nobody created." },
            TestContext.Current.CancellationToken);

        await AssertNotFound(response);
    }

    /// <summary>
    /// The 404 half of the matrix. Both routes declare it and both reach it through
    /// <c>NotFoundException</c>, so what is asserted here is the status and the <c>type</c> slug a
    /// caller is handed — neither of which the handler and query tests can see.
    /// </summary>
    private static async Task AssertNotFound(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement problem = await ReadAsync(response);

        Assert.Equal(ProblemTypes.NotFound, problem.GetProperty("type").GetString());
        Assert.Equal(StatusCodes.Status404NotFound, problem.GetProperty("status").GetInt32());
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument
            .Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .RootElement
            .Clone();

    /// <summary>
    /// The three persistence ports, in memory. Nothing here interprets the request: the store
    /// holds whatever the aggregate produced, so the assertions above are about the Api ring.
    /// </summary>
    private sealed class MeetingStore : IMeetingRepository, IUnitOfWork, IReadDb
    {
        private readonly List<Meeting> _pending = [];
        private readonly List<Meeting> _committed = [];

        public void Replace(IServiceCollection services)
        {
            services.RemoveAll<IMeetingRepository>();
            services.RemoveAll<IUnitOfWork>();
            services.RemoveAll<IReadDb>();

            services.AddSingleton<IMeetingRepository>(this);
            services.AddSingleton<IUnitOfWork>(this);
            services.AddSingleton<IReadDb>(this);
        }

        public void Add(Meeting meeting) => _pending.Add(meeting);

        public Task<Meeting?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_committed.Find(meeting => meeting.Id == id));

        public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            int written = _pending.Count;

            _committed.AddRange(_pending);
            _pending.Clear();

            return Task.FromResult(written);
        }

        public IQueryable<T> Query<T>()
            where T : class => _committed.OfType<T>().AsQueryable();

        public Task<IReadOnlyList<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>([.. query]);

        public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Count());
    }
}
