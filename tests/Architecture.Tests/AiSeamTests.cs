using System.Reflection;
using System.Text.Json.Nodes;
using ActionLedger.Application;
using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Ai;
using ActionLedger.Infrastructure;
using Xunit;

namespace ActionLedger.Architecture.Tests;

/// <summary>
/// AD-6, AD-11, AD-21 — the extraction seam's shape, pinned where a container would otherwise be
/// the first thing to notice it broke.
/// </summary>
/// <remarks>
/// <para>
/// Two claims are load bearing and invisible at compile time. First, exactly one type implements
/// <see cref="IActionExtractor"/>: a second extraction path is a path that can skip validation.
/// Second, every prompt and catalog file is embedded under its AD-6/AD-21 logical name with the
/// bytes the file on disk holds — a silently dropped <c>EmbeddedResource</c> builds, links, and
/// passes every unit test that reads the resources it did keep, and fails only when the api image
/// runs.
/// </para>
/// <para>
/// <c>ProjectFile.RepositoryRoot</c> is <c>internal</c> to this assembly, which is why the
/// disk-versus-resource comparison lives here rather than in <c>Infrastructure.Tests</c>.
/// </para>
/// </remarks>
public sealed class AiSeamTests
{
    private const string PromptFolder = "prompts";

    private const string CatalogFolder = "fixtures/extraction";

    private const string SchemaPath = "src/ActionLedger.Application/Ai/extract-actions.schema.json";

    private static readonly Assembly ApplicationAssembly = typeof(ApplicationAssemblyMarker).Assembly;

    private static readonly Assembly InfrastructureAssembly = typeof(InfrastructureAssemblyMarker).Assembly;

    // --- One extractor (AD-11) -----------------------------------------------------------------

    [Fact]
    public void Exactly_one_type_implements_the_extraction_port()
    {
        string[] implementations =
        [
            .. InfrastructureAssembly.GetTypes()
                .Where(type => type is { IsAbstract: false, IsInterface: false })
                .Where(type => typeof(IActionExtractor).IsAssignableFrom(type))
                .Select(type => type.FullName!)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(["ActionLedger.Infrastructure.Ai.ChatClientActionExtractor"], implementations);
    }

    [Fact]
    public void No_ring_below_infrastructure_implements_the_extraction_port()
    {
        Assert.DoesNotContain(
            ApplicationAssembly.GetTypes(),
            type => type is { IsAbstract: false, IsInterface: false } && typeof(IActionExtractor).IsAssignableFrom(type));
    }

    // --- The prompt files (AD-6) ---------------------------------------------------------------

    [Fact]
    public void Every_prompt_file_is_embedded_under_its_logical_name_with_the_bytes_on_disk()
    {
        AssertEmbeddedVerbatim(InfrastructureAssembly, PromptFolder, DiskFiles(PromptFolder));
    }

    [Fact]
    public void The_v1_prompt_is_embedded()
    {
        Assert.Contains("prompts/extract-actions.v1.md", InfrastructureAssembly.GetManifestResourceNames());
    }

    // --- The fixture catalog (AD-21) -----------------------------------------------------------

    /// <summary>
    /// Every catalog file except <c>README.md</c>, which is documentation of the format rather than
    /// a case and would fail the loader's pairing rule if the <c>Exclude</c> were dropped.
    /// </summary>
    [Fact]
    public void Every_catalog_file_except_the_readme_is_embedded_with_the_bytes_on_disk()
    {
        string[] expected =
        [
            .. DiskFiles(CatalogFolder).Where(name => !name.Equals("README.md", StringComparison.Ordinal)),
        ];

        AssertEmbeddedVerbatim(InfrastructureAssembly, CatalogFolder, expected);
    }

    [Fact]
    public void The_catalog_readme_is_not_embedded()
    {
        Assert.DoesNotContain($"{CatalogFolder}/README.md", InfrastructureAssembly.GetManifestResourceNames());
    }

    [Fact]
    public void The_catalog_embeds_fourteen_cases_and_their_answers_plus_the_roster()
    {
        string[] embedded =
        [
            .. InfrastructureAssembly.GetManifestResourceNames()
                .Where(name => name.StartsWith($"{CatalogFolder}/", StringComparison.Ordinal)),
        ];

        Assert.Equal(14, embedded.Count(name => name.EndsWith(".md", StringComparison.Ordinal)));
        Assert.Equal(14, embedded.Count(name => name.EndsWith(".expected.json", StringComparison.Ordinal)));
        Assert.Contains($"{CatalogFolder}/roster.json", embedded);
        Assert.Equal(29, embedded.Length);
    }

    // --- The committed schema (AD-6) -----------------------------------------------------------

    [Fact]
    public void The_schema_is_embedded_in_the_application_assembly_with_the_bytes_on_disk()
    {
        Assert.Contains(ExtractionSchema.ResourceName, ApplicationAssembly.GetManifestResourceNames());

        Assert.Equal(
            File.ReadAllBytes(Path.Combine(ProjectFile.RepositoryRoot.FullName, SchemaPath)),
            ResourceBytes(ApplicationAssembly, ExtractionSchema.ResourceName));
    }

    /// <summary>
    /// AD-6 — <c>SchemaVersion</c> is the schema file's own top-level <c>version</c>. Removing that
    /// member from the file is what this goes red on.
    /// </summary>
    [Fact]
    public void The_schema_version_reported_is_the_one_the_committed_file_declares()
    {
        JsonObject committed = JsonNode
            .Parse(File.ReadAllText(Path.Combine(ProjectFile.RepositoryRoot.FullName, SchemaPath)))!
            .AsObject();

        Assert.Equal(committed["version"]?.GetValue<string>(), ExtractionSchema.Version);
    }

    [Fact]
    public void The_schema_is_embedded_in_no_other_assembly()
    {
        Assert.DoesNotContain(
            InfrastructureAssembly.GetManifestResourceNames(),
            name => name.EndsWith("extract-actions.schema.json", StringComparison.Ordinal));
    }

    // --- Helpers -------------------------------------------------------------------------------

    private static void AssertEmbeddedVerbatim(Assembly assembly, string folder, IReadOnlyList<string> files)
    {
        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            string logicalName = $"{folder}/{file}";

            Assert.Contains(logicalName, assembly.GetManifestResourceNames());

            Assert.Equal(
                File.ReadAllBytes(Path.Combine(ProjectFile.RepositoryRoot.FullName, folder, file)),
                ResourceBytes(assembly, logicalName));
        }

        Assert.Equal(
            files.Order(StringComparer.Ordinal),
            assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith($"{folder}/", StringComparison.Ordinal))
                .Select(name => name[(folder.Length + 1)..])
                .Order(StringComparer.Ordinal));
    }

    private static string[] DiskFiles(string folder) =>
        [
            .. new DirectoryInfo(Path.Combine(ProjectFile.RepositoryRoot.FullName, folder))
                .EnumerateFiles()
                .Select(file => file.Name)
                .Order(StringComparer.Ordinal),
        ];

    private static byte[] ResourceBytes(Assembly assembly, string logicalName)
    {
        using Stream resource = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"'{logicalName}' is not embedded in {assembly.GetName().Name}.");

        using MemoryStream buffer = new();

        resource.CopyTo(buffer);

        return buffer.ToArray();
    }
}
