using System.Reflection;
using System.Xml.Linq;
using ActionLedger.Api;
using ActionLedger.Application;
using ActionLedger.Domain;
using ActionLedger.Infrastructure;
using NetArchTest.Rules;
using Xunit;

// NetArchTest and xunit both ship a TestResult; AD-1 is asserted against NetArchTest's.
using TestResult = NetArchTest.Rules.TestResult;

namespace ActionLedger.Architecture.Tests;

/// <summary>
/// AD-1 — dependency direction is enforced by a test.
///
/// Each of the six rules is asserted two ways, because neither way alone is sufficient:
///
///   * NetArchTest, against the real compiled assemblies, catches a ring that *uses* a type it
///     must not see. This is the check that matters once the rings hold code.
///   * A project-file scan catches a `PackageReference` or `ProjectReference` that has been added
///     but not yet used. Roslyn prunes unused assembly references out of the emitted metadata, so
///     no assembly-level check can see a freshly added package — and "Domain takes a package"
///     must go red on the commit that adds it, not on the commit that first calls into it.
///
/// There is no fixture that genuinely violates AD-1: writing one would mean committing a project
/// that breaks the build. The rules assert zero violations against the real solution instead, and
/// go red the moment someone adds the forbidden reference.
/// </summary>
public sealed class DependencyRuleTests
{
    private static readonly Assembly DomainAssembly = typeof(DomainAssemblyMarker).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(ApplicationAssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(InfrastructureAssemblyMarker).Assembly;
    private static readonly Assembly ApiAssembly = typeof(ApiAssemblyMarker).Assembly;

    private const string DomainProject = "ActionLedger.Domain";
    private const string ApplicationProject = "ActionLedger.Application";
    private const string InfrastructureProject = "ActionLedger.Infrastructure";
    private const string ApiProject = "ActionLedger.Api";

    /// <summary>The only packages AD-1 lets the Application ring take.</summary>
    private static readonly string[] ApplicationPackageAllowlist =
    [
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "System.Text.Json",
    ];

    /// <summary>Namespace roots the two inner rings may never see. Also the package-id prefixes.</summary>
    private static readonly string[] OuterWorldNamespaces =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "OpenAI",
        "Microsoft.Extensions.AI",
    ];

    // ---------------------------------------------------------------------------------------
    // Rule 1 — Domain references nothing: no package, no project.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Rule1_domain_declares_no_package_and_no_project_reference()
    {
        ProjectFile domain = ProjectFile.Load(DomainProject);

        Assert.Empty(domain.PackageReferences);
        Assert.Empty(domain.ProjectReferences);
    }

    [Fact]
    public void Rule1_domain_types_depend_on_no_other_ring()
    {
        TestResult result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(ApplicationProject, InfrastructureProject, ApiProject)
            .GetResult();

        AssertNoViolations(result, "Domain must not depend on any other ring");
    }

    // ---------------------------------------------------------------------------------------
    // Rule 2 — Application references Domain only, plus the three allowed packages.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Rule2_application_references_domain_only()
    {
        ProjectFile application = ProjectFile.Load(ApplicationProject);

        Assert.Equal([DomainProject], application.ProjectReferences);
    }

    [Fact]
    public void Rule2_application_takes_no_package_outside_the_allowlist()
    {
        ProjectFile application = ProjectFile.Load(ApplicationProject);

        string[] outsideAllowlist = [.. application.PackageReferences.Except(ApplicationPackageAllowlist)];

        Assert.Empty(outsideAllowlist);
    }

    [Fact]
    public void Rule2_application_types_depend_on_no_outer_ring()
    {
        TestResult result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(InfrastructureProject, ApiProject)
            .GetResult();

        AssertNoViolations(result, "Application must not depend on Infrastructure or Api");
    }

    // ---------------------------------------------------------------------------------------
    // Rule 3 — Neither inner ring reaches EF Core, ASP.NET Core, Npgsql, OpenAI, or Microsoft.Extensions.AI.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(DomainProject)]
    [InlineData(ApplicationProject)]
    public void Rule3_inner_rings_declare_no_persistence_web_or_ai_package(string project)
    {
        ProjectFile ring = ProjectFile.Load(project);

        string[] forbidden =
        [
            .. ring.PackageReferences.Where(package =>
                OuterWorldNamespaces.Any(prefix =>
                    package.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                    package.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))),
        ];

        Assert.Empty(forbidden);
    }

    [Fact]
    public void Rule3_inner_ring_types_never_touch_persistence_web_or_ai()
    {
        TestResult result = Types.InAssemblies([DomainAssembly, ApplicationAssembly])
            .Should()
            .NotHaveDependencyOnAny(OuterWorldNamespaces)
            .GetResult();

        AssertNoViolations(result, "Domain and Application must not reference EF Core, ASP.NET Core, Npgsql, OpenAI, or Microsoft.Extensions.AI");
    }

    // ---------------------------------------------------------------------------------------
    // Rule 4 — Nothing in src references Api.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Rule4_no_src_project_references_api()
    {
        string[] offenders =
        [
            .. ProjectFile.LoadAllSrcProjects()
                .Where(project => project.Name != ApiProject)
                .Where(project => project.ProjectReferences.Contains(ApiProject))
                .Select(project => project.Name),
        ];

        Assert.Empty(offenders);
    }

    [Fact]
    public void Rule4_no_inner_ring_type_depends_on_api()
    {
        TestResult result = Types.InAssemblies([DomainAssembly, ApplicationAssembly, InfrastructureAssembly])
            .Should()
            .NotHaveDependencyOnAny(ApiProject)
            .GetResult();

        AssertNoViolations(result, "No ring below Api may depend on Api");
    }

    // ---------------------------------------------------------------------------------------
    // Rule 5 — Normalization lives in Application/Ai and nowhere else.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Rule5_normalization_types_live_only_in_application_ai()
    {
        TestResult result = Types
            .InAssemblies([DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ApiAssembly])
            .That()
            .HaveNameMatching("Normaliz")
            .Should()
            .ResideInNamespace($"{ApplicationProject}.Ai")
            .GetResult();

        AssertNoViolations(result, $"Types named *Normaliz* belong in {ApplicationProject}.Ai");
    }

    // ---------------------------------------------------------------------------------------
    // Rule 6 — Infrastructure references Application and Domain only.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Rule6_infrastructure_references_application_and_domain_only()
    {
        ProjectFile infrastructure = ProjectFile.Load(InfrastructureProject);

        string[] outsideTheRings =
        [
            .. infrastructure.ProjectReferences.Except([ApplicationProject, DomainProject]),
        ];

        Assert.Empty(outsideTheRings);
    }

    [Fact]
    public void Rule6_infrastructure_types_depend_on_no_outer_ring()
    {
        TestResult result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOnAny(ApiProject)
            .GetResult();

        AssertNoViolations(result, "Infrastructure must not depend on Api");
    }

    private static void AssertNoViolations(TestResult result, string rule)
    {
        if (result.IsSuccessful)
        {
            return;
        }

        IEnumerable<string> offenders = result.FailingTypes?.Select(type => type.FullName) ?? [];

        Assert.Fail($"{rule}. Offending types: {string.Join(", ", offenders)}");
    }
}
