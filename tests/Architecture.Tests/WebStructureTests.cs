using System.CodeDom.Compiler;
using System.Reflection;
using ActionLedger.Web;
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
