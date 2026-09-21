using System.Xml.Linq;

namespace ActionLedger.Architecture.Tests;

/// <summary>
/// The declared references of one src project, read from MSBuild rather than from metadata.
///
/// Roslyn omits unused assembly references from the assemblies it emits, so a `PackageReference`
/// that has been added but not yet called is invisible to every assembly-level check. AD-1 has to
/// catch it on the commit that adds it, which means reading the project files.
///
/// `Directory.Build.props` and `Directory.Build.targets` between the repository root and the
/// project are folded in, so a package smuggled into a shared props file counts against the ring
/// that ends up carrying it.
/// </summary>
internal sealed record ProjectFile(string Name, string[] PackageReferences, string[] ProjectReferences)
{
    private static readonly string[] SharedImportFileNames = ["Directory.Build.props", "Directory.Build.targets"];

    internal static ProjectFile Load(string projectName)
    {
        string projectDirectory = Path.Combine(RepositoryRoot.FullName, "src", projectName);
        string projectPath = Path.Combine(projectDirectory, projectName + ".csproj");

        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException($"Expected the {projectName} ring at {projectPath}.", projectPath);
        }

        return Load(new FileInfo(projectPath));
    }

    internal static IReadOnlyList<ProjectFile> LoadAllSrcProjects() =>
    [
        .. new DirectoryInfo(Path.Combine(RepositoryRoot.FullName, "src"))
            .EnumerateFiles("*.csproj", SearchOption.AllDirectories)
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .Select(Load),
    ];

    private static ProjectFile Load(FileInfo projectPath)
    {
        List<XDocument> documents = [XDocument.Load(projectPath.FullName)];
        documents.AddRange(SharedImportsFor(projectPath.Directory!));

        string[] packages =
        [
            .. documents
                .SelectMany(document => Includes(document, "PackageReference"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal),
        ];

        string[] projects =
        [
            .. documents
                .SelectMany(document => Includes(document, "ProjectReference"))
                .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal),
        ];

        return new ProjectFile(Path.GetFileNameWithoutExtension(projectPath.Name), packages, projects);
    }

    /// <summary>Every shared props/targets file from the project directory up to the repository root.</summary>
    private static IEnumerable<XDocument> SharedImportsFor(DirectoryInfo projectDirectory)
    {
        for (DirectoryInfo? directory = projectDirectory; directory is not null; directory = directory.Parent)
        {
            foreach (string fileName in SharedImportFileNames)
            {
                string candidate = Path.Combine(directory.FullName, fileName);

                if (File.Exists(candidate))
                {
                    yield return XDocument.Load(candidate);
                }
            }

            if (string.Equals(directory.FullName, RepositoryRoot.FullName, StringComparison.Ordinal))
            {
                yield break;
            }
        }
    }

    /// <summary>`Include` attributes only — `Update` refines an item that some other file declared.</summary>
    private static IEnumerable<string> Includes(XDocument document, string itemName) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!.Trim());

    private static readonly DirectoryInfo RepositoryRoot = FindRepositoryRoot();

    private static DirectoryInfo FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ActionLedger.sln")))
            {
                return directory;
            }
        }

        throw new InvalidOperationException(
            $"Could not find ActionLedger.sln above {AppContext.BaseDirectory}. The AD-1 project-file rules cannot run.");
    }
}
