using ActionLedger.Application.Auth;
using ActionLedger.Application.Extraction;
using ActionLedger.Application.Meetings;
using ActionLedger.Application.Review;
using ActionLedger.Application.Users;
using Microsoft.Extensions.DependencyInjection;

namespace ActionLedger.Application;

/// <summary>
/// What the composition root wires up for this ring: the use-case handlers and the per-feature
/// query classes. Every later story adds its handler or query here and nowhere else.
/// </summary>
/// <remarks>
/// The ring owns its own registration rather than letting the Api enumerate its types, which is
/// why <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> is on AD-1's allowlist. Only
/// abstractions: the container implementation stays in the host.
/// </remarks>
public static class ApplicationRegistration
{
    /// <summary>Registers every handler and query in this ring.</summary>
    public static IServiceCollection AddActionLedgerApplication(this IServiceCollection services)
    {
        services.AddScoped<SignInHandler>();
        services.AddScoped<UsersQueries>();

        services.AddScoped<CreateMeetingHandler>();
        services.AddScoped<SaveMeetingNotesHandler>();
        services.AddScoped<MeetingsQueries>();

        // Story 2.5 — the extraction use case and the two reads behind it. ProposedActionReadModel
        // is registered in its own right because RunsQueries composes it and the Epic 3 Review
        // Screen will resolve the same instance type (AD-9).
        services.AddScoped<RunExtractionHandler>();
        services.AddScoped<RunsQueries>();
        services.AddScoped<ProposedActionReadModel>();

        return services;
    }
}
