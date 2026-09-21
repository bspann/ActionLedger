using ActionLedger.Web.Core.Api;
using ActionLedger.Web.Core.Users;
using Xunit;

namespace ActionLedger.Web.Tests;

/// <summary>
/// AD-14 — the roster is the second of the two cross-feature state services, and it loads once.
/// The "once" is otherwise unobservable: nothing renders the roster until Epic 3's owner picker,
/// so only a call count can tell a cached roster from one re-fetched on every navigation.
/// </summary>
public sealed class UserDirectoryTests
{
    [Fact]
    public async Task The_roster_is_fetched_as_one_full_page()
    {
        // Paging.MaxPageSize is 200 and pageSize is clamped to it, so one page holds the roster.
        StubApiClient client = Roster();
        UserDirectory directory = new(client);

        await directory.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, client.LastPage);
        Assert.Equal(200, client.LastPageSize);
    }

    [Fact]
    public async Task The_roster_is_mapped_onto_web_owned_records()
    {
        StubApiClient client = Roster();
        UserDirectory directory = new(client);

        await directory.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        DirectoryUser user = Assert.Single(directory.Users);
        Assert.Equal(Guid.Parse("01999999-0000-7000-8000-00000000abcd"), user.Id);
        Assert.Equal("Marcus Bell", user.DisplayName);

        // A string, not the generated Role enum — the picker that renders this in Epic 3 sits
        // above the AD-14 seam.
        Assert.Equal(RoleNames.Lead, user.Role);
        Assert.True(directory.IsLoaded);
    }

    [Theory]
    [InlineData(Role.Lead, "Lead")]
    [InlineData(Role.ActionOfficer, "Action Officer")]
    public void The_glossarys_wording_is_produced_in_one_place(Role role, string expected)
    {
        // Two of the three seeded demo users are Action Officers, so the wire spelling would be
        // on screen for most of the demo. Pinned here because both AuthService and UserDirectory
        // draw from it and neither can check what the other produced.
        Assert.Equal(expected, RoleNames.Display(role));
    }

    [Fact]
    public async Task An_action_officers_role_is_mapped_for_reading()
    {
        StubApiClient client = Roster();
        client.Roster.Items.Single().Role = Role.ActionOfficer;
        UserDirectory directory = new(client);

        await directory.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RoleNames.ActionOfficer, directory.Users.Single().Role);
    }

    [Fact]
    public async Task A_timed_out_roster_call_is_swallowed_too()
    {
        // Uncaught this escapes EnsureLoadedAsync into LoginPage's sign-in, and the user ends up
        // signed in but stranded on the form with no navigation — over a roster nothing renders
        // until Epic 3.
        StubApiClient client = new() { ListUsersThrows = new TaskCanceledException("the request timed out") };
        UserDirectory directory = new(client);

        await directory.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        Assert.Empty(directory.Users);
        Assert.False(directory.IsLoaded);
    }

    [Fact]
    public async Task A_second_call_does_not_refetch()
    {
        StubApiClient client = Roster();
        UserDirectory directory = new(client);

        await directory.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        await directory.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, client.ListUsersCalls);
    }

    [Fact]
    public async Task A_failed_load_leaves_an_empty_roster_and_does_not_throw()
    {
        // The roster has no consumer until Epic 3, so a failure here must not block a sign-in or
        // hold up the shell.
        StubApiClient client = new() { ListUsersThrows = StubApiClient.Bare(500) };
        UserDirectory directory = new(client);

        await directory.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        Assert.Empty(directory.Users);

        // False on purpose: the first real consumer can retry rather than inherit a silently
        // empty list.
        Assert.False(directory.IsLoaded);
    }

    [Fact]
    public async Task A_transport_failure_is_swallowed_too()
    {
        StubApiClient client = new() { ListUsersThrows = new HttpRequestException("no route to host") };
        UserDirectory directory = new(client);

        await directory.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        Assert.Empty(directory.Users);
        Assert.False(directory.IsLoaded);
    }

    [Fact]
    public async Task A_failed_load_can_be_retried()
    {
        StubApiClient client = new() { ListUsersThrows = StubApiClient.Bare(500) };
        UserDirectory directory = new(client);

        await directory.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        client.ListUsersThrows = null;
        client.Roster = Roster().Roster;
        await directory.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        Assert.True(directory.IsLoaded);
        Assert.Single(directory.Users);
    }

    [Fact]
    public async Task Clearing_empties_the_roster_so_the_next_session_reloads_it()
    {
        StubApiClient client = Roster();
        UserDirectory directory = new(client);
        await directory.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        directory.Clear();

        Assert.Empty(directory.Users);
        Assert.False(directory.IsLoaded);

        await directory.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, client.ListUsersCalls);
    }

    private static StubApiClient Roster() => new()
    {
        Roster = new PagedResultOfUserSummaryDto
        {
            Items =
            [
                new UserSummaryDto
                {
                    Id = Guid.Parse("01999999-0000-7000-8000-00000000abcd"),
                    DisplayName = "Marcus Bell",
                    Role = Role.Lead,
                },
            ],
            Page = 1,
            PageSize = 200,
            Total = 1,
        },
    };
}
