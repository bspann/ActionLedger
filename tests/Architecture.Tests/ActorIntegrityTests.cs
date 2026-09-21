using System.Reflection;
using ActionLedger.Application;
using Xunit;

namespace ActionLedger.Architecture.Tests;

/// <summary>
/// AD-12 and FR-25 — the actor is the token's <c>sub</c> claim, never a value the caller supplies.
///
/// <c>ICurrentUser</c> is tested directly in <c>Api.Tests/CurrentUserTests</c>, which establishes that
/// the actor is read from the claim. This class holds the other half of the same acceptance criterion:
/// that no handler command or query parameter offers a caller somewhere to put an actor id in the
/// first place. Nothing else enforces it. The invariant is true of the handlers story 1.4 ships, and
/// story 1.4 is the story every later handler is copied from, so an added <c>ActorId</c> would
/// otherwise reach the review stage with a green suite behind it.
///
/// The ban is by exact member name, not by substring, and that is deliberate. A command naming a
/// *target* user — <c>OwnerUserId</c>, <c>AssigneeUserId</c> — is legitimate and passes: Epic 3
/// resolves an owner on approval, and that owner is not the actor. A bare <c>UserId</c> on a command
/// is the ambiguous case, so it is refused; naming the target explicitly both satisfies this rule and
/// reads better at the call site.
/// </summary>
public sealed class ActorIntegrityTests
{
    private static readonly Assembly ApplicationAssembly = typeof(ApplicationAssemblyMarker).Assembly;

    /// <summary>
    /// Member names that mean "whoever is making this request". Matched whole and case-insensitively.
    /// </summary>
    private static readonly HashSet<string> ActorIdentityNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Actor", "ActorId", "ActorUserId",
        "CurrentUser", "CurrentUserId",
        "ActingUser", "ActingUserId",
        "User", "UserId",
        "Subject", "SubjectId",
        "CreatedBy", "CreatedById", "CreatedByUserId",
        "PerformedBy", "PerformedById", "PerformedByUserId",
        "RequestedBy", "RequestedById", "RequestedByUserId",
        "OnBehalfOf", "OnBehalfOfUserId",
    };

    /// <summary>Every type a <c>*Handler.HandleAsync</c> accepts as its command.</summary>
    private static IReadOnlyList<Type> CommandTypes() =>
    [
        .. ApplicationAssembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .SelectMany(handler => handler.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(method => method.Name == "HandleAsync")
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Where(type => type.Assembly == ApplicationAssembly)
            .Distinct(),
    ];

    /// <summary>Every public method on a <c>*Queries</c> class.</summary>
    private static IReadOnlyList<MethodInfo> QueryMethods() =>
    [
        .. ApplicationAssembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Queries", StringComparison.Ordinal))
            .SelectMany(queries => queries.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)),
    ];

    [Fact]
    public void A_handler_command_never_carries_the_actor_that_sent_it()
    {
        IReadOnlyList<Type> commands = CommandTypes();

        // Without this the rule passes loudest on the day someone renames the handler suffix and the
        // enumeration silently finds nothing to inspect.
        Assert.NotEmpty(commands);

        List<string> offenders =
        [
            .. from command in commands
               from member in command.GetMembers(BindingFlags.Public | BindingFlags.Instance)
               where member is PropertyInfo or FieldInfo
               where ActorIdentityNames.Contains(member.Name)
               select $"{command.Name}.{member.Name}",
        ];

        Assert.True(
            offenders.Count == 0,
            $"The actor comes from the token's sub claim (AD-12, FR-25), never from the request body. "
                + $"Remove or rename: {string.Join(", ", offenders)}. "
                + $"A member naming a *target* user rather than the caller — OwnerUserId, AssigneeUserId — is allowed.");
    }

    [Fact]
    public void A_query_never_takes_the_actor_as_a_parameter()
    {
        IReadOnlyList<MethodInfo> methods = QueryMethods();

        Assert.NotEmpty(methods);

        List<string> offenders =
        [
            .. from method in methods
               from parameter in method.GetParameters()
               where parameter.Name is not null && ActorIdentityNames.Contains(parameter.Name)
               select $"{method.DeclaringType?.Name}.{method.Name}({parameter.Name})",
        ];

        Assert.True(
            offenders.Count == 0,
            $"A query scopes itself from ICurrentUser, not from a caller-supplied actor (AD-12, FR-25). "
                + $"Remove or rename: {string.Join(", ", offenders)}.");
    }
}
