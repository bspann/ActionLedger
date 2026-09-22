using System.Text.RegularExpressions;
using Xunit;

namespace ActionLedger.Architecture.Tests;

/// <summary>
/// AD-8 / AD-21 — <c>SaveChangesAsync(suppressOutbox: true)</c> skips the outbox pipeline, so an
/// approval saved through it would reach no webhook receiver. Only the seeder, whose decisions are
/// history rather than news, may call it.
/// </summary>
/// <remarks>
/// <para>
/// A source scan rather than a reflection rule: a call site is text, and no metadata records who
/// calls an overload. Suppressing takes the named argument — a bare <c>bool</c> cannot bind to the
/// flag — so every call is a <c>suppressOutbox:</c>, however it is spaced or wrapped.
/// </para>
/// <para>
/// Comment lines are skipped, because a remark that names the call is not a call. The parameter's
/// declaration in <c>AppDbContext</c> is skipped too, by its line's shape rather than by excluding
/// the file, so a call added anywhere else in that file is still caught.
/// </para>
/// </remarks>
public sealed partial class OutboxSuppressionTests
{
    private static readonly string SourceRoot = Path.Combine(ProjectFile.RepositoryRoot.FullName, "src");

    private static readonly string SeedDirectory =
        Path.Combine(SourceRoot, "ActionLedger.Infrastructure", "Seed") + Path.DirectorySeparatorChar;

    [GeneratedRegex(@"\bsuppressOutbox\s*:")]
    private static partial Regex NamedArgument();

    [GeneratedRegex(@"\bbool\s+suppressOutbox\b")]
    private static partial Regex ParameterDeclaration();

    /// <summary>Every <c>file:line</c> under <c>src/</c> that passes the flag, build output and comments excluded.</summary>
    private static IReadOnlyList<(string Path, int Line)> CallSites() =>
    [
        .. from path in Directory.EnumerateFiles(SourceRoot, "*.cs", SearchOption.AllDirectories)
           where !IsBuildOutput(path)
           from numbered in File.ReadLines(path).Select((text, index) => (Text: text, Line: index + 1))
           let trimmed = numbered.Text.TrimStart()
           where !trimmed.StartsWith("//", StringComparison.Ordinal)
           where !ParameterDeclaration().IsMatch(numbered.Text)
           where NamedArgument().IsMatch(numbered.Text)
           select (path, numbered.Line),
    ];

    [Fact]
    public void Only_the_seeder_suppresses_the_outbox()
    {
        IReadOnlyList<(string Path, int Line)> sites = CallSites();

        // Without this the rule passes loudest on the day the seeder's call is renamed away and the
        // scan silently finds nothing to inspect.
        Assert.Contains(sites, site => site.Path.StartsWith(SeedDirectory, StringComparison.Ordinal));

        List<string> offenders =
        [
            .. sites
                .Where(site => !site.Path.StartsWith(SeedDirectory, StringComparison.Ordinal))
                .Select(site => $"{Path.GetRelativePath(ProjectFile.RepositoryRoot.FullName, site.Path)}:{site.Line}"),
        ];

        Assert.True(
            offenders.Count == 0,
            $"Only src/ActionLedger.Infrastructure/Seed/ may pass suppressOutbox: — it writes no outbox row. Found at: {string.Join(", ", offenders)}");
    }

    private static bool IsBuildOutput(string path)
    {
        string[] segments = Path.GetRelativePath(SourceRoot, path).Split(Path.DirectorySeparatorChar);

        return segments.Contains("bin", StringComparer.Ordinal) || segments.Contains("obj", StringComparer.Ordinal);
    }
}
