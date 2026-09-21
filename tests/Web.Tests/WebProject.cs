namespace ActionLedger.Web.Tests;

/// <summary>
/// Locates files inside <c>src/ActionLedger.Web</c> from a test. The walk up to the directory
/// holding <c>ActionLedger.sln</c> is the same one <c>Architecture.Tests/ProjectFile</c> and
/// <c>OpenApiExport</c> use, so there is one notion of "the repository root" per assembly and no
/// test depends on the working directory it was launched from.
/// </summary>
internal static class WebProject
{
    internal static string ReadAllText(string relativePath)
    {
        string path = Path.Combine(
            Root.FullName,
            "src",
            "ActionLedger.Web",
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Expected {relativePath} in the web project at {path}.", path);
        }

        return File.ReadAllText(path);
    }

    private static readonly DirectoryInfo Root = FindRepositoryRoot();

    private static DirectoryInfo FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ActionLedger.sln")))
            {
                return directory;
            }
        }

        throw new InvalidOperationException(
            $"Could not find ActionLedger.sln above {AppContext.BaseDirectory}. The token tests cannot run.");
    }
}
