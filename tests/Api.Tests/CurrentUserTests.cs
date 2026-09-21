using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using ActionLedger.Api.Auth;
using ActionLedger.Application.Abstractions;
using ActionLedger.Domain.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// AD-12 and FR-25 — the actor is the token's <c>sub</c> claim, read through
/// <see cref="ICurrentUser"/> and never from a body or a parameter. The token here is one the
/// running host issued, validated with the running host's own parameters, so what is asserted is
/// the round trip rather than a principal a test assembled to suit itself.
/// </summary>
public sealed class CurrentUserTests
{
    [Fact]
    public async Task The_current_user_is_the_sub_name_and_role_of_a_token_this_api_issued()
    {
        await using TestApi api = new();
        using HttpClient _ = api.CreateClient();

        User marcus = User.Register(
            "marcus",
            "Marcus Bell",
            "hash-of-marcus",
            Role.Lead,
            DateTimeOffset.UnixEpoch);

        AccessToken issued = api.Services.GetRequiredService<IAccessTokenIssuer>().Issue(marcus);

        ICurrentUser current = new ClaimsPrincipalCurrentUser(AccessorFor(Validate(api, issued.Token)));

        Assert.Equal(marcus.Id, current.UserId);
        Assert.Equal("Marcus Bell", current.DisplayName);
        Assert.Equal(Role.Lead, current.Role);
    }

    [Fact]
    public async Task The_container_resolves_a_fresh_actor_per_request_not_one_shared_between_them()
    {
        // Constructing the class by hand proves what it reads; only driving it through the host
        // proves it is registered, resolvable behind [Authorize], and scoped. A singleton would
        // hand the second request the first request's claims, and a missing registration would
        // fail to resolve at all.
        await using TestApi api = new() { IncludeTestEndpoints = true };
        using HttpClient client = api.CreateClient();

        (Guid Subject, string DisplayName, string Role)[] callers =
        [
            (Guid.CreateVersion7(), "Dana Whitfield", nameof(Domain.Users.Role.ActionOfficer)),
            (Guid.CreateVersion7(), "Marcus Bell", nameof(Domain.Users.Role.Lead)),
        ];

        foreach ((Guid subject, string displayName, string role) in callers)
        {
            client.DefaultRequestHeaders.Authorization =
                new("Bearer", TestApi.TokenFor(role, subject.ToString(), displayName));

            HttpResponseMessage response = await client.GetAsync(
                $"/{Routing.ApiRoutes.Prefix}/protected/current-user",
                TestContext.Current.CancellationToken);

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

            JsonElement actor = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

            Assert.Equal(subject, actor.GetProperty("userId").GetGuid());
            Assert.Equal(displayName, actor.GetProperty("displayName").GetString());
            Assert.Equal(role, actor.GetProperty("role").GetString());
        }
    }

    [Fact]
    public void An_unauthenticated_request_has_no_current_user_rather_than_an_invented_one()
    {
        // No principal is not "the anonymous actor". A write attributed to a guessed identity is
        // worse than a failed request, so this throws and there is no fallback to fall back to.
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            new ClaimsPrincipalCurrentUser(AccessorFor(new ClaimsPrincipal(new ClaimsIdentity()))));

        Assert.Contains("no authenticated user", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_request_with_no_http_context_at_all_has_no_current_user()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ClaimsPrincipalCurrentUser(new HttpContextAccessor()));
    }

    [Fact]
    public void An_authenticated_principal_missing_the_subject_claim_is_refused()
    {
        ClaimsIdentity identity = new(
            [new Claim(JwtBearerSetup.NameClaim, "Nobody"), new Claim(JwtBearerSetup.RoleClaim, nameof(Role.Lead))],
            authenticationType: "Test");

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            new ClaimsPrincipalCurrentUser(AccessorFor(new ClaimsPrincipal(identity))));

        Assert.Contains(JwtBearerSetup.SubjectClaim, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(JwtBearerSetup.SubjectClaim, "not-a-guid")]
    // TryParse alone returns true here, which would make (Role)99 an authenticated actor holding
    // a role that does not exist anywhere in the Domain.
    [InlineData(JwtBearerSetup.RoleClaim, "99")]
    [InlineData(JwtBearerSetup.RoleClaim, "Archivist")]
    public void A_claim_this_api_never_issues_is_refused_rather_than_coerced(string claim, string value)
    {
        Dictionary<string, string> claims = new(StringComparer.Ordinal)
        {
            [JwtBearerSetup.SubjectClaim] = Guid.CreateVersion7().ToString(),
            [JwtBearerSetup.NameClaim] = "Dana Whitfield",
            [JwtBearerSetup.RoleClaim] = nameof(Role.ActionOfficer),
            [claim] = value,
        };

        ClaimsIdentity identity = new(
            [.. claims.Select(pair => new Claim(pair.Key, pair.Value))],
            authenticationType: "Test");

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            new ClaimsPrincipalCurrentUser(AccessorFor(new ClaimsPrincipal(identity))));

        Assert.Contains(claim, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates a token with the parameters the bearer scheme itself uses, so the principal is
    /// the one a real request would carry — including the wire claim names AD-12 fixes.
    /// </summary>
    private static ClaimsPrincipal Validate(TestApi api, string token)
    {
        TokenValidationParameters parameters = api.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .TokenValidationParameters;

        // The scheme sets MapInboundClaims = false; a handler that remapped `sub` here would be
        // testing something the host never does.
        JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };

        return handler.ValidateToken(token, parameters, out _);
    }

    private static IHttpContextAccessor AccessorFor(ClaimsPrincipal principal) =>
        new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
}
