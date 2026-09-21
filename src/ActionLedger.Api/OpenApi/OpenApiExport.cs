using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ActionLedger.Api.OpenApi;

/// <summary>
/// AD-13 — <c>openapi.json</c> is generated, never hand-written, and committed at
/// <c>src/ActionLedger.Web/openapi.json</c>. This is the generator behind
/// <c>dotnet run --project src/ActionLedger.Api -- --export-openapi</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>OpenApiSnapshotTest</c> calls <see cref="GenerateAsync"/> through
/// <c>WebApplicationFactory</c> and compares the result to the committed file. Export and test
/// therefore run the same code, so a passing snapshot means a re-export is genuinely a no-op
/// rather than a formatting coincidence.
/// </para>
/// <para>
/// The host never listens in export mode: the app is built so its endpoints and transformers are
/// in place, the document is read out of the service provider, and the process exits.
/// </para>
/// </remarks>
public static class OpenApiExport
{
    /// <summary>The flag that switches the process from "serve" to "write the contract and exit".</summary>
    public const string Flag = "--export-openapi";

    /// <summary>Where the contract is committed, relative to the repository root.</summary>
    public const string ContractRelativePath = "src/ActionLedger.Web/openapi.json";

    /// <summary>
    /// Splits <c>--export-openapi [path]</c> off the command line. The flag is removed before the
    /// host sees the arguments, because the command-line configuration provider would otherwise
    /// try to read it as a configuration key.
    /// </summary>
    public static (string? OutputPath, bool Requested, string[] HostArgs) ParseCommandLine(string[] args)
    {
        int flag = Array.IndexOf(args, Flag);

        if (flag < 0)
        {
            return (null, false, args);
        }

        bool hasPath = flag + 1 < args.Length && !args[flag + 1].StartsWith('-');
        string? path = hasPath ? args[flag + 1] : null;
        int consumed = hasPath ? 2 : 1;

        string[] hostArgs = [.. args.Take(flag), .. args.Skip(flag + consumed)];

        return (path, true, hostArgs);
    }

    /// <summary>
    /// Writes the document to <paramref name="outputPath"/>, or to the committed contract path
    /// when none is given.
    /// </summary>
    /// <returns>A process exit code: 0 on success, 1 when the document could not be written.</returns>
    public static async Task<int> WriteAsync(
        WebApplication app,
        string? outputPath,
        CancellationToken cancellationToken = default)
    {
        AttachEndpoints(app);

        string destination;

        try
        {
            destination = outputPath is null
                ? CommittedContractPath()
                : Path.GetFullPath(outputPath);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            await Console.Error.WriteLineAsync($"Could not resolve the contract path: {exception.Message}");
            return 1;
        }

        try
        {
            string json = await GenerateAsync(app.Services, cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllTextAsync(destination, json, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await Console.Error.WriteLineAsync($"Could not write {destination}: {exception.Message}");
            return 1;
        }

        Console.WriteLine($"Wrote {destination}");

        return 0;
    }

    /// <summary>
    /// Serializes the generated document exactly as the committed file stores it: OpenAPI 3.1,
    /// JSON, with a trailing newline so the file is a well-formed text file and <c>git diff</c>
    /// stays quiet.
    /// </summary>
    public static async Task<string> GenerateAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        IOpenApiDocumentProvider provider =
            services.GetKeyedService<IOpenApiDocumentProvider>(OpenApiSetup.DocumentName)
            ?? services.GetRequiredService<IOpenApiDocumentProvider>();

        OpenApiDocument document = await provider.GetOpenApiDocumentAsync(cancellationToken);

        string json = await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, cancellationToken);

        return json.ReplaceLineEndings("\n").TrimEnd('\n') + "\n";
    }

    /// <summary>
    /// Finds <c>src/ActionLedger.Web/openapi.json</c> by walking up to the directory holding
    /// <c>ActionLedger.sln</c>, so the export lands in the repository whether it was started from
    /// the solution root, the project directory, or the build output.
    /// </summary>
    public static string CommittedContractPath() =>
        Path.Combine(RepositoryRoot().FullName, ContractRelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Publishes the routes this host maps to the endpoint data source the document generator
    /// reads. Mapping a route only records it on the application; it is wired into routing when
    /// the pipeline is assembled, which normally happens as the host starts to listen.
    /// </summary>
    private static void AttachEndpoints(WebApplication app)
    {
        app.UseRouting();
        ((IApplicationBuilder)app).UseEndpoints(_ => { });
    }

    private static DirectoryInfo RepositoryRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ActionLedger.sln")))
                {
                    return directory;
                }
            }
        }

        throw new InvalidOperationException(
            $"Could not find ActionLedger.sln above {AppContext.BaseDirectory} or {Directory.GetCurrentDirectory()}. "
            + $"Pass an explicit path: {Flag} <path>.");
    }
}
