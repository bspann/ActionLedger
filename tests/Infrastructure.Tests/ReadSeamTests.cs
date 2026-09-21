using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Users;
using ActionLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ActionLedger.Infrastructure.Tests;

/// <summary>
/// AD-2 — <c>IReadDb</c> is the seam every later query class composes over, and reads through it
/// never track. Asserted against a real change tracker, because that is the only thing that can
/// tell a tracked read from an untracked one: a LINQ-to-Objects fake has no tracker at all, so
/// <c>UsersQueriesTests</c> would pass either way.
/// </summary>
[Collection(PostgresFixture.CollectionName)]
public sealed class ReadSeamTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_read_through_the_seam_leaves_the_change_tracker_empty()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        string connectionString = await postgres.CreateDatabaseAsync(
            nameof(A_read_through_the_seam_leaves_the_change_tracker_empty),
            cancellationToken);

        await using ServiceProvider services = TestHost.Build(connectionString, TestHost.SeedingOff);

        await TestHost.MigrateAsync(services, cancellationToken);

        await using (AsyncServiceScope write = services.CreateAsyncScope())
        {
            write.ServiceProvider.GetRequiredService<IUserRepository>().Add(User.Register(
                "dana",
                "Dana Whitfield",
                "not-a-real-hash",
                Role.ActionOfficer,
                DateTimeOffset.UnixEpoch));

            await write.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(cancellationToken);
        }

        // A fresh scope, so the tracker starts empty and what is in it afterwards is this read.
        await using AsyncServiceScope read = services.CreateAsyncScope();

        IReadDb readDb = read.ServiceProvider.GetRequiredService<IReadDb>();
        AppDbContext context = read.ServiceProvider.GetRequiredService<AppDbContext>();

        IReadOnlyList<User> roster = await readDb.ToListAsync(readDb.Query<User>(), cancellationToken);

        // The read really happened...
        Assert.Equal("Dana Whitfield", Assert.Single(roster).DisplayName);

        // ...and left nothing behind that a later CommitAsync in the same scope could pick up and
        // write back. Without AsNoTracking this is one entry.
        Assert.Empty(context.ChangeTracker.Entries());
    }
}
