using ActionLedger.Web.Core.Api;

namespace ActionLedger.Web.Core.Users;

/// <summary>A person work can be assigned to, in web-owned types (AD-14).</summary>
public sealed record DirectoryUser(Guid Id, string DisplayName, string Role);

/// <summary>
/// AD-14's second named cross-feature state service: the roster loads once after sign-in and is
/// held for the session.
/// </summary>
/// <remarks>
/// Nothing renders the roster until Epic 3's owner picker, which makes a failed load a non-event
/// — it must not block a successful sign-in or hold up the shell, so the failure is swallowed
/// into an empty roster on purpose. <see cref="IsLoaded"/> stays false in that case, so the first
/// real consumer can retry rather than inheriting a silently empty list.
/// </remarks>
public sealed class UserDirectory(IActionLedgerApiClient client)
{
    /// <summary>The 1-based first page. The server clamps <c>pageSize</c> at 200.</summary>
    private const int FirstPage = 1;

    /// <summary>The server's <c>Paging.MaxPageSize</c>. The demo roster is three people.</summary>
    private const int RosterPageSize = 200;

    private IReadOnlyList<DirectoryUser> users = [];

    /// <summary>True only after a load that actually returned a roster.</summary>
    public bool IsLoaded { get; private set; }

    public IReadOnlyList<DirectoryUser> Users => users;

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoaded)
        {
            return;
        }

        try
        {
            PagedResultOfUserSummaryDto page =
                await client.ListUsersAsync(FirstPage, RosterPageSize, cancellationToken).ConfigureAwait(false);

            users =
            [
                .. page.Items.Select(item =>
                    new DirectoryUser(item.Id, item.DisplayName, RoleNames.Display(item.Role))),
            ];

            IsLoaded = true;
        }
        catch (ApiException)
        {
            users = [];
        }
        catch (HttpRequestException)
        {
            users = [];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timed-out roster call throws TaskCanceledException. Left uncaught it escapes into
            // LoginPage's sign-in, which would leave the user signed in but stranded on the form
            // with no navigation — over a roster nothing renders until Epic 3.
            users = [];
        }
    }

    /// <summary>Called on sign-out, so the next person to sign in does not inherit this roster.</summary>
    public void Clear()
    {
        users = [];
        IsLoaded = false;
    }
}
