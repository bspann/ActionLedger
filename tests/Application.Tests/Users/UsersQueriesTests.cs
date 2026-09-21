using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Users;
using ActionLedger.Domain.Users;
using Xunit;

namespace ActionLedger.Application.Tests.Users;

/// <summary>
/// AD-2 and the Paging convention (AD-13) — the first <c>&lt;Feature&gt;Queries</c>: a read over
/// <see cref="IReadDb"/> that filters system users out, orders deterministically, clamps the
/// window through <see cref="Paging.Normalize"/>, and counts every matching row rather than the
/// page it returned.
/// </summary>
public sealed class UsersQueriesTests
{
    private static readonly DateTimeOffset Registered = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_system_identity_is_not_in_the_roster()
    {
        UsersQueries queries = Over(
            Human("dana", "Dana Whitfield", Role.ActionOfficer),
            System("seed", "Seed"));

        PagedResult<UserSummaryDto> roster = await queries.ListAsync(
            page: null,
            pageSize: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(["Dana Whitfield"], roster.Items.Select(user => user.DisplayName));

        // The excluded row is excluded from the count too, not merely from this page.
        Assert.Equal(1, roster.Total);
    }

    [Fact]
    public async Task The_roster_is_ordered_by_display_name()
    {
        UsersQueries queries = Over(
            Human("marcus", "Marcus Bell", Role.Lead),
            Human("dana", "Dana Whitfield", Role.ActionOfficer),
            Human("priya", "Priya Ramaswamy", Role.ActionOfficer));

        PagedResult<UserSummaryDto> roster = await queries.ListAsync(
            page: null,
            pageSize: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Dana Whitfield", "Marcus Bell", "Priya Ramaswamy"],
            roster.Items.Select(user => user.DisplayName));
    }

    [Fact]
    public async Task Two_people_sharing_a_display_name_still_page_in_a_stable_order()
    {
        // Paging without a total order returns rows twice and skips others. The id is the
        // tie-break that makes the order total.
        User[] namesakes =
        [
            Human("dana", "Dana Whitfield", Role.ActionOfficer),
            Human("dana.w", "Dana Whitfield", Role.Lead),
        ];

        UsersQueries queries = Over(namesakes);

        PagedResult<UserSummaryDto> first = await queries.ListAsync(
            page: 1,
            pageSize: 1,
            TestContext.Current.CancellationToken);

        PagedResult<UserSummaryDto> second = await queries.ListAsync(
            page: 2,
            pageSize: 1,
            TestContext.Current.CancellationToken);

        Guid[] expected = [.. namesakes.Select(user => user.Id).Order()];
        Guid[] paged = [first.Items.Single().Id, second.Items.Single().Id];

        Assert.Equal(expected, paged);
    }

    [Fact]
    public async Task A_window_outside_the_bounds_is_clamped_rather_than_refused()
    {
        UsersQueries queries = Over(Human("dana", "Dana Whitfield", Role.ActionOfficer));

        PagedResult<UserSummaryDto> roster = await queries.ListAsync(
            page: 0,
            pageSize: 9999,
            TestContext.Current.CancellationToken);

        Assert.Equal(Paging.FirstPage, roster.Page);
        Assert.Equal(Paging.MaxPageSize, roster.PageSize);

        // The envelope echoes the window that was applied, not the one that was asked for.
        Assert.Single(roster.Items);
    }

    [Fact]
    public async Task Total_counts_every_matching_row_not_the_length_of_the_page()
    {
        UsersQueries queries = Over(
            Human("dana", "Dana Whitfield", Role.ActionOfficer),
            Human("priya", "Priya Ramaswamy", Role.ActionOfficer),
            Human("marcus", "Marcus Bell", Role.Lead),
            System("seed", "Seed"));

        PagedResult<UserSummaryDto> roster = await queries.ListAsync(
            page: 2,
            pageSize: 2,
            TestContext.Current.CancellationToken);

        Assert.Equal(["Priya Ramaswamy"], roster.Items.Select(user => user.DisplayName));
        Assert.Equal(3, roster.Total);
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_and_still_reports_the_total()
    {
        UsersQueries queries = Over(Human("dana", "Dana Whitfield", Role.ActionOfficer));

        PagedResult<UserSummaryDto> roster = await queries.ListAsync(
            page: 9,
            pageSize: 50,
            TestContext.Current.CancellationToken);

        Assert.Empty(roster.Items);
        Assert.Equal(1, roster.Total);
        Assert.Equal(9, roster.Page);
    }

    [Fact]
    public async Task A_page_far_past_the_end_is_empty_rather_than_an_overflowed_offset()
    {
        UsersQueries queries = Over(Human("dana", "Dana Whitfield", Role.ActionOfficer));

        // (page - 1) * pageSize overflows int well before this, which sent PostgreSQL a negative
        // OFFSET and 500'd a read any authenticated caller can reach.
        PagedResult<UserSummaryDto> roster = await queries.ListAsync(
            page: int.MaxValue,
            pageSize: Paging.MaxPageSize,
            TestContext.Current.CancellationToken);

        Assert.Empty(roster.Items);
        Assert.Equal(1, roster.Total);
        Assert.Equal(int.MaxValue, roster.Page);
        Assert.Equal(Paging.MaxPageSize, roster.PageSize);
    }

    private static UsersQueries Over(params User[] users) => new(new FakeReadDb(users));

    private static User Human(string username, string displayName, Role role) =>
        User.Register(username, displayName, $"hash-of-{username}", role, Registered);

    private static User System(string username, string displayName) =>
        User.RegisterSystem(username, displayName, $"hash-of-{username}", Registered);

    /// <summary>
    /// The read seam over a list. LINQ to Objects composes the same <c>Where</c>/<c>OrderBy</c>/
    /// <c>Skip</c>/<c>Take</c>/<c>Select</c> the provider translates, so what is asserted here is
    /// the query the class builds; that it also translates to SQL is the Api suite's job.
    /// </summary>
    private sealed class FakeReadDb(params object[] rows) : IReadDb
    {
        public IQueryable<T> Query<T>()
            where T : class => rows.OfType<T>().AsQueryable();

        public Task<IReadOnlyList<T>> ToListAsync<T>(
            IQueryable<T> query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>([.. query]);

        public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Count());
    }
}
