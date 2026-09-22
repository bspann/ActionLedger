using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ActionLedger.Api.Auth;
using ActionLedger.Api.Configuration;
using ActionLedger.Api.Controllers;
using ActionLedger.Api.Errors;
using ActionLedger.Application;
using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Auth;
using ActionLedger.Application.Users;
using ActionLedger.Domain.Users;
using ActionLedger.Infrastructure;
using ActionLedger.Infrastructure.Persistence;
using ActionLedger.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// AD-12, AD-13, AD-18 — sign-in and the roster end to end: a real <c>postgres:18-alpine</c>, the
/// real seeder, the real bearer scheme. The load-bearing assertion is that the token this Api
/// issues is one this Api accepts; nothing short of a round trip proves that.
/// </summary>
public sealed class AuthEndpointTests(SeededApi seeded) : IClassFixture<SeededApi>
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly string LoginPath = $"/{Routing.ApiRoutes.Prefix}/auth/login";
    private static readonly string UsersPath = $"/{Routing.ApiRoutes.Prefix}/users";

    [Fact]
    public async Task A_seeded_user_signs_in_and_the_token_is_one_the_running_host_accepts()
    {
        using HttpClient client = seeded.Api.CreateClient();

        SignInResult signedIn = await SignInAsync(client, SeededApi.SomeoneWhoSignsIn.Username, seeded.Password);

        Assert.False(string.IsNullOrWhiteSpace(signedIn.Token));
        Assert.Equal(SeededApi.SomeoneWhoSignsIn.DisplayName, signedIn.User.DisplayName);
        Assert.Equal(SeededApi.SomeoneWhoSignsIn.Role, signedIn.User.Role);

        // The whole point of the story: take the token straight back to a protected route.
        client.DefaultRequestHeaders.Authorization = new("Bearer", signedIn.Token);

        HttpResponseMessage roster = await client.GetAsync(UsersPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, roster.StatusCode);
    }

    [Fact]
    public async Task The_issued_token_is_hs256_with_sub_name_and_role_no_audience_and_eight_hours()
    {
        using HttpClient client = seeded.Api.CreateClient();

        SignInResult signedIn = await SignInAsync(client, SeededApi.SomeoneWhoSignsIn.Username, seeded.Password);

        JwtSecurityToken token = new JwtSecurityTokenHandler().ReadJwtToken(signedIn.Token);

        Assert.Equal(SecurityAlgorithms.HmacSha256, token.Header.Alg);
        Assert.Equal(TestApi.Issuer, token.Issuer);

        // No Jwt:Audience key exists and ValidateAudience is deliberately false, so claiming an
        // audience here would be describing a check nothing performs.
        Assert.Empty(token.Audiences);

        Assert.Equal(signedIn.User.Id.ToString(), token.Claims.Single(c => c.Type == JwtBearerSetup.SubjectClaim).Value);
        Assert.Equal(signedIn.User.DisplayName, token.Claims.Single(c => c.Type == JwtBearerSetup.NameClaim).Value);
        Assert.Equal(signedIn.User.Role.ToString(), token.Claims.Single(c => c.Type == JwtBearerSetup.RoleClaim).Value);

        Assert.Equal(
            JwtOptions.TokenLifetime,
            token.ValidTo - token.ValidFrom);

        // `expiresAt` in the body is the same instant the token carries, to the second.
        Assert.Equal(token.ValidTo, signedIn.ExpiresAt.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    /// <summary>How a seeded username might be typed at the login screen.</summary>
    public enum Typing
    {
        AsStored,
        Uppercased,
        Capitalized,
        Padded,
    }

    [Theory]
    [InlineData(Typing.AsStored)]
    [InlineData(Typing.Uppercased)]
    [InlineData(Typing.Capitalized)]
    [InlineData(Typing.Padded)]
    public async Task A_username_is_matched_as_stored_so_casing_and_padding_do_not_matter(Typing typing)
    {
        using HttpClient client = seeded.Api.CreateClient();

        // Derived from the seeded username rather than from a literal. Rewriting a literal into
        // the stored form would erase the very casing under test, and renaming a demo user would
        // turn this row into a 401 that looks like a real regression.
        string stored = SeededApi.SomeoneWhoSignsIn.Username;

        string typed = typing switch
        {
            Typing.AsStored => stored,
            Typing.Uppercased => stored.ToUpperInvariant(),
            Typing.Capitalized => string.Concat(stored[..1].ToUpperInvariant(), stored[1..]),
            Typing.Padded => $"  {stored} ",
            _ => throw new ArgumentOutOfRangeException(nameof(typing)),
        };

        SignInResult signedIn = await SignInAsync(client, typed, seeded.Password);

        Assert.Equal(SeededApi.SomeoneWhoSignsIn.DisplayName, signedIn.User.DisplayName);
    }

    [Fact]
    public async Task A_wrong_password_an_unknown_username_and_the_system_identity_are_one_401()
    {
        using HttpClient client = seeded.Api.CreateClient();

        string wrongPassword = await RefusalBodyAsync(client, SeededApi.SomeoneWhoSignsIn.Username, TestApi.NewPassword());
        string unknownUser = await RefusalBodyAsync(client, "nobody-by-that-name", seeded.Password);

        // AD-21's system identity owns seeded rows; it is not an account, and saying so in the
        // body would be telling a caller that the username exists.
        string systemIdentity = await RefusalBodyAsync(client, DemoDataSeeder.SystemUsername, seeded.Password);

        Assert.Equal(wrongPassword, unknownUser);
        Assert.Equal(wrongPassword, systemIdentity);

        JsonElement problem = JsonDocument.Parse(wrongPassword).RootElement;

        Assert.Equal(ProblemTypes.Unauthorized, problem.GetProperty("type").GetString());
        Assert.Equal(AuthController.RefusedDetail, problem.GetProperty("detail").GetString());
    }

    /// <summary>The ways a login body can be malformed rather than merely wrong.</summary>
    public enum Malformation
    {
        BlankUsername,
        BlankPassword,
        OverlongPassword,
    }

    [Theory]
    [InlineData(Malformation.BlankUsername)]
    [InlineData(Malformation.BlankPassword)]
    [InlineData(Malformation.OverlongPassword)]
    public async Task A_malformed_credential_is_a_400_validation_not_a_401(Malformation malformation)
    {
        using HttpClient client = seeded.Api.CreateClient();

        (string username, string password) = malformation switch
        {
            Malformation.BlankUsername => (string.Empty, TestApi.NewPassword()),
            Malformation.BlankPassword => (SeededApi.SomeoneWhoSignsIn.Username, string.Empty),
            // Refused by model validation, so an anonymous caller cannot hand PBKDF2 an
            // arbitrarily large body for a username the demo publishes.
            Malformation.OverlongPassword => (
                SeededApi.SomeoneWhoSignsIn.Username,
                new string('x', SignInCommand.PasswordMaxLength + 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(malformation)),
        };

        HttpResponseMessage response = await client.PostAsJsonAsync(
            LoginPath,
            new { username, password },
            TestContext.Current.CancellationToken);

        // A missing field is a malformed request, not a refused credential. Answering 401 would
        // tell a caller their empty string was merely the wrong password.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        JsonElement problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

        Assert.Equal(ProblemTypes.Validation, problem.GetProperty("type").GetString());
    }

    [Fact]
    public async Task The_roster_refuses_an_anonymous_caller()
    {
        using HttpClient client = seeded.Api.CreateClient();

        HttpResponseMessage response = await client.GetAsync(UsersPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_roster_is_every_seeded_human_ordered_by_display_name_and_no_system_identity()
    {
        using HttpClient client = await SignedInClientAsync();

        PagedResult<UserSummaryDto> roster = await GetRosterAsync(client, UsersPath);

        string[] expected = [.. DemoDataSeeder.DemoUsers.Select(user => user.DisplayName).Order(StringComparer.Ordinal)];

        Assert.Equal(expected, roster.Items.Select(user => user.DisplayName).ToArray());

        // The contract publishes Role as a string enum. JsonStringEnumConverter reads numbers too,
        // so deserializing would not notice if the wire went back to integers — the raw body does.
        Assert.Contains(
            $"\"role\":\"{Role.ActionOfficer}\"",
            await RawRosterAsync(client),
            StringComparison.Ordinal);
        Assert.Equal(expected.Length, roster.Total);

        // The Seed User is in the table and out of the roster.
        Assert.DoesNotContain(DemoDataSeeder.SystemDisplayName, roster.Items.Select(user => user.DisplayName));
    }

    [Fact]
    public async Task The_roster_clamps_the_paging_window_and_counts_every_matching_row()
    {
        using HttpClient client = await SignedInClientAsync();

        PagedResult<UserSummaryDto> roster = await GetRosterAsync(client, $"{UsersPath}?page=0&pageSize=9999");

        Assert.Equal(Paging.FirstPage, roster.Page);
        Assert.Equal(Paging.MaxPageSize, roster.PageSize);
        Assert.Equal(DemoDataSeeder.DemoUsers.Count, roster.Total);

        PagedResult<UserSummaryDto> firstOnly = await GetRosterAsync(client, $"{UsersPath}?page=1&pageSize=1");

        Assert.Single(firstOnly.Items);

        // `total` is every matching row, not the length of the page that was returned.
        Assert.Equal(DemoDataSeeder.DemoUsers.Count, firstOnly.Total);
    }

    private static async Task<string> RawRosterAsync(HttpClient client) =>
        await (await client.GetAsync(UsersPath, TestContext.Current.CancellationToken))
            .Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    private async Task<HttpClient> SignedInClientAsync()
    {
        HttpClient client = seeded.Api.CreateClient();

        SignInResult signedIn = await SignInAsync(client, SeededApi.SomeoneWhoSignsIn.Username, seeded.Password);

        client.DefaultRequestHeaders.Authorization = new("Bearer", signedIn.Token);

        return client;
    }

    private static async Task<SignInResult> SignInAsync(HttpClient client, string username, string password)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            LoginPath,
            new { username, password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<SignInResult>(Json, TestContext.Current.CancellationToken))!;
    }

    private static async Task<PagedResult<UserSummaryDto>> GetRosterAsync(HttpClient client, string path)
    {
        HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<PagedResult<UserSummaryDto>>(
            Json,
            TestContext.Current.CancellationToken))!;
    }

    /// <summary>
    /// The refusal body, with the one field that is allowed to differ removed. The correlation id
    /// is per-request by design; everything else — status, type, title, detail, instance — has to
    /// be byte-identical across the three ways a sign-in can fail.
    /// </summary>
    private static async Task<string> RefusalBodyAsync(HttpClient client, string username, string password)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            LoginPath,
            new { username, password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonNode body = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;

        body.AsObject().Remove(Observability.RequestCorrelation.PropertyName);

        return body.ToJsonString();
    }
}

/// <summary>
/// One <c>postgres:18-alpine</c>, migrated once, with one seeded host on top of it for the whole
/// class. AD-17 — the api never migrates itself, so the schema is applied here by a standalone
/// context before the host that must not do it is ever started.
/// </summary>
public sealed class SeededApi : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("actionledger")
        .WithUsername("actionledger")
        .WithPassword("actionledger-tests-not-a-secret")
        .Build();

    private ServiceProvider? _migrator;

    /// <summary>The first seeded human. Read from the seeder, never from a literal roster.</summary>
    public static DemoDataSeeder.DemoUser SomeoneWhoSignsIn => DemoDataSeeder.DemoUsers[0];

    /// <summary>
    /// The password this run seeded with. NFR5 — invented per run, so no assertion here can pass
    /// by agreeing with a value committed to the repository.
    /// </summary>
    public string Password { get; } = TestApi.NewPassword();

    private TestApi? _api;

    /// <summary>The seeded host. Available once initialization succeeded.</summary>
    public TestApi Api =>
        _api ?? throw new InvalidOperationException("The seeded host did not start; see the initialization failure.");

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await _postgres.StartAsync(cancellationToken);

        string connectionString = _postgres.GetConnectionString();

        await MigrateAsync(connectionString, cancellationToken);

        _api = new TestApi
        {
            ConfigurationOverrides =
            {
                ["Database:ConnectionString"] = connectionString,
                ["Seed:Enabled"] = "true",
                ["Seed:DefaultPassword"] = Password,
            },
        };

        // Starting the host is what runs the seeder; every test in the class then shares the rows.
        Api.CreateClient().Dispose();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        // Every field is disposed only if it was ever created. A container or migration failure
        // otherwise surfaces as a NullReferenceException from here, hiding the real cause.
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        if (_migrator is not null)
        {
            await _migrator.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    private async Task MigrateAsync(string connectionString, CancellationToken cancellationToken)
    {
        ServiceCollection services = new();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddActionLedgerPersistence(_ => connectionString);

        // AppDbContext takes the outbox's payload builder, which the Application ring registers.
        services.AddActionLedgerApplication();

        _migrator = services.BuildServiceProvider();

        await using AsyncServiceScope scope = _migrator.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
