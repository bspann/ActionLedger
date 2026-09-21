using ActionLedger.Api.OpenApi;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// AD-13 — the committed <c>openapi.json</c> is the contract between the Api and the web app.
/// Story 1.5 generates the Angular client from that file, so a route or DTO that changes without
/// a re-export would leave the web build compiling against a contract the Api no longer serves.
/// This test is what makes "regenerate and commit" a rule rather than a habit.
/// </summary>
public sealed class OpenApiSnapshotTest
{
    [Fact]
    public async Task Committed_contract_matches_the_generated_document()
    {
        await using TestApi api = new();

        // Boot the host. Endpoints are attached to routing as the host starts, and the document
        // is generated from them.
        using HttpClient _ = api.CreateClient();

        string generated = await OpenApiExport.GenerateAsync(api.Services, TestContext.Current.CancellationToken);

        string contractPath = OpenApiExport.CommittedContractPath();

        Assert.True(
            File.Exists(contractPath),
            $"{OpenApiExport.ContractRelativePath} is missing. Run: dotnet run --project src/ActionLedger.Api -- --export-openapi");

        string committed = (await File.ReadAllTextAsync(contractPath, TestContext.Current.CancellationToken))
            .ReplaceLineEndings("\n");

        if (!string.Equals(committed, generated, StringComparison.Ordinal))
        {
            Assert.Fail(
                $"{OpenApiExport.ContractRelativePath} is out of date.\n"
                + "Run: dotnet run --project src/ActionLedger.Api -- --export-openapi\n\n"
                + FirstDifference(committed, generated));
        }
    }

    [Fact]
    public async Task Contract_contains_no_route_from_the_test_assembly()
    {
        await using TestApi api = new();
        using HttpClient _ = api.CreateClient();

        string generated = await OpenApiExport.GenerateAsync(api.Services, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("/errors/", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("/protected/", generated, StringComparison.Ordinal);
    }

    /// <summary>Points at the first differing line, so a failure reads as a diff rather than as two blobs.</summary>
    private static string FirstDifference(string committed, string generated)
    {
        string[] left = committed.Split('\n');
        string[] right = generated.Split('\n');

        for (int line = 0; line < Math.Max(left.Length, right.Length); line++)
        {
            string committedLine = line < left.Length ? left[line] : "<end of file>";
            string generatedLine = line < right.Length ? right[line] : "<end of file>";

            if (!string.Equals(committedLine, generatedLine, StringComparison.Ordinal))
            {
                return $"First difference at line {line + 1}:\n"
                    + $"  committed: {committedLine}\n"
                    + $"  generated: {generatedLine}";
            }
        }

        return "The files differ only in trailing whitespace.";
    }
}
