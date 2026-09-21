using System.CodeDom.Compiler;
using System.Reflection;
using ActionLedger.Web;
using Microsoft.AspNetCore.Components;
using NetArchTest.Rules;
using Xunit;

// NetArchTest and xunit both ship a TestResult; the rule is asserted against NetArchTest's.
using TestResult = NetArchTest.Rules.TestResult;

namespace ActionLedger.Architecture.Tests;

/// <summary>
/// AD-14 — HTTP happens only in <c>Core/</c> and <c>Features/*/Data/</c>, and the five feature
/// folders exist. This is the build-enforced replacement for the lint rule that went away with
/// the old frontend project in the 2026-09-21 sprint change: build-enforced rather than
/// lint-enforced, so it cannot be skipped.
/// </summary>
public sealed class WebStructureTests
{
    private static readonly Assembly WebAssembly = typeof(WebAssemblyMarker).Assembly;

    /// <summary>The generated client's namespace, and the framework namespace it is built on.</summary>
    private static readonly string[] TheHttpSeam = ["System.Net.Http", "ActionLedger.Web.Core.Api"];

    /// <summary>
    /// <c>Core</c> and <c>Features.&lt;anything&gt;.Data</c>, each with their descendants. Folder
    /// layout and namespace agree because <c>RootNamespace</c> is <c>ActionLedger.Web</c>.
    /// </summary>
    private const string SeamNamespaces = @"^ActionLedger\.Web\.(Core|Features\.[^.]+\.Data)(\..+)?$";

    /// <summary>The five feature folders AD-14 names, each with its Data seam.</summary>
    private static readonly string[] Features = ["Auth", "Meetings", "Review", "Actions", "Audit"];

    /// <summary>Where AD-14 puts a routable component, and what it calls one.</summary>
    private const string FeatureNamespacePrefix = "ActionLedger.Web.Features.";

    private const string PageSuffix = "Page";

    /// <summary>
    /// The exact namespace a routable component may sit in: one of the five feature folders, and
    /// nothing nested under it. A prefix match would accept <c>Features.Auth.Data</c> — the HTTP
    /// seam, which is the one place a page must never be — and <c>Features.Reports</c>, a sixth
    /// feature that exists in the namespace but not on disk, so the folder rule never sees it.
    /// </summary>
    private static readonly string[] FeatureNamespaces =
        [.. Features.Select(feature => FeatureNamespacePrefix + feature)];

    [Fact]
    public void Http_is_confined_to_core_and_the_per_feature_data_folders()
    {
        TestResult result = Types.InAssembly(WebAssembly)
            .That()
            .HaveDependencyOnAny(TheHttpSeam)
            .Should()
            .ResideInNamespaceMatching(SeamNamespaces)
            .GetResult();

        if (result.IsSuccessful)
        {
            return;
        }

        IEnumerable<string?> offenders = result.FailingTypes?.Select(type => type.FullName) ?? [];

        Assert.Fail(
            "AD-14: HttpClient and the generated client may only be referenced from Core/ and "
            + $"Features/*/Data/. Offending types: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void The_http_rule_has_something_to_rule_on()
    {
        // HaveDependencyOnAny over a tree where nothing touches HTTP yields an empty set, and
        // NetArchTest reports success on it, so the rule above needs a subject to be worth
        // anything. Counting every type in the seam would not supply one: the generated client
        // lives in Core/Api and is built on System.Net.Http, so it matches the seam on its own
        // and would keep this green forever. What has to exist is a *hand-written* consumer —
        // the registration and the per-feature data services. Delete the last of those and the
        // rule stops protecting anything, which is exactly when this must go red.
        string[] handWrittenInTheSeam =
        [
            .. Types.InAssembly(WebAssembly)
                .That()
                .HaveDependencyOnAny(TheHttpSeam)
                .GetTypes()
                .Select(type => type.ReflectionType)
                .Where(type => type.GetCustomAttribute<GeneratedCodeAttribute>() is null)
                .Where(type => type.DeclaringType is null)
                .Select(type => type.FullName ?? type.Name),
        ];

        Assert.NotEmpty(handWrittenInTheSeam);
    }

    [Fact]
    public void Every_routable_component_is_a_page_under_a_feature_folder()
    {
        // AD-14: "one routable container component per route in a feature folder", named
        // <Noun>Page.razor. Nothing enforced it before — a page dropped into Shared/ or Layout/,
        // or one named LoginScreen, passed every other check here, and the naming convention is
        // how a reader tells a routable container from a presentational child at a glance.
        Type[] routable =
        [
            .. WebAssembly.GetTypes()
                .Where(type => type.GetCustomAttributes<RouteAttribute>(inherit: false).Any()),
        ];

        // The rule needs a subject. Until Story 1.6 the assembly held no @page at all, and an
        // empty set would keep this green through every later story that moved one.
        Assert.NotEmpty(routable);

        string[] offenders =
        [
            .. routable
                .Where(type => !IsRoutableWhereAd14SaysItShouldBe(type.Namespace, type.Name))
                .Select(type => type.FullName ?? type.Name),
        ];

        Assert.True(
            offenders.Length == 0,
            $"AD-14: every [Route] component must sit in exactly one of {string.Join(", ", FeatureNamespaces)} "
            + $"and be named <Noun>Page. Offending types: {string.Join(", ", offenders)}");
    }

    [Theory]
    // Where a page belongs.
    [InlineData("ActionLedger.Web.Features.Auth", "LoginPage", true)]
    [InlineData("ActionLedger.Web.Features.Actions", "ActionDetailPage", true)]
    // Inside a feature, but in the HTTP seam — the one folder a page must never be in.
    [InlineData("ActionLedger.Web.Features.Auth.Data", "LoginPage", false)]
    // A sixth feature that exists as a namespace but not as a folder, so the folder rule that
    // pins the five names on disk never sees it.
    [InlineData("ActionLedger.Web.Features.Reports", "ReportsPage", false)]
    // Outside Features/ altogether.
    [InlineData("ActionLedger.Web.Shared", "LoginPage", false)]
    [InlineData("ActionLedger.Web.Layout", "LoginPage", false)]
    // Right place, wrong name.
    [InlineData("ActionLedger.Web.Features.Auth", "LoginScreen", false)]
    [InlineData(null, "LoginPage", false)]
    public void The_routable_component_rule_accepts_exactly_the_ad14_shape(string? space, string name, bool expected)
    {
        // The rule above can only ever see the pages this assembly happens to hold, so the shapes
        // it is meant to reject are unreachable from it. A prefix match would pass the two middle
        // rows here while still claiming to enforce "one routable component per route in a
        // feature folder".
        Assert.Equal(expected, IsRoutableWhereAd14SaysItShouldBe(space, name));
    }

    private static bool IsRoutableWhereAd14SaysItShouldBe(string? space, string name) =>
        FeatureNamespaces.Contains(space, StringComparer.Ordinal)
        && name.EndsWith(PageSuffix, StringComparison.Ordinal);

    [Fact]
    public void The_web_project_references_no_other_project()
    {
        ProjectFile web = ProjectFile.Load("ActionLedger.Web");

        // AD-13 — the committed openapi.json is the boundary between the Api and the web app,
        // and the typed client is generated from it. A ProjectReference to Application would
        // let the web app use the server's DTOs directly, which compiles, passes every other
        // test here, and quietly removes the boundary this whole arrangement exists to create.
        // Rule4 only catches a reference to Api; this catches a reference to anything.
        Assert.Empty(web.ProjectReferences);
    }

    [Fact]
    public void The_generated_client_folder_is_ignored_by_version_control()
    {
        string[] ignored =
        [
            .. File.ReadAllLines(Path.Combine(ProjectFile.RepositoryRoot.FullName, ".gitignore"))
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#')),
        ];

        // AD-13 — the client is regenerated from the contract on every build and is never
        // committed. Nothing else fails when the ignore rule goes: the build still passes and
        // the 50 KB generated file simply starts appearing in diffs and merge conflicts.
        Assert.Contains("src/ActionLedger.Web/Core/Api/", ignored, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("Auth")]
    [InlineData("Meetings")]
    [InlineData("Review")]
    [InlineData("Actions")]
    [InlineData("Audit")]
    public void The_feature_folder_exists_with_its_data_seam(string feature)
    {
        string data = Path.Combine(
            ProjectFile.RepositoryRoot.FullName, "src", "ActionLedger.Web", "Features", feature, "Data");

        Assert.True(Directory.Exists(data), $"AD-14 names the feature folder {feature}, but {data} does not exist.");
    }

    [Fact]
    public void No_feature_folder_outside_the_five_ad14_names_exists()
    {
        string[] onDisk =
        [
            .. new DirectoryInfo(
                    Path.Combine(ProjectFile.RepositoryRoot.FullName, "src", "ActionLedger.Web", "Features"))
                .EnumerateDirectories()
                .Select(directory => directory.Name)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal([.. Features.Order(StringComparer.Ordinal)], onDisk);
    }
}
