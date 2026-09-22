using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ActionLedger.Application.Ai;

namespace ActionLedger.Infrastructure.Ai;

/// <summary>
/// AD-21 — the embedded fixture catalog, and the Fake provider's answer table.
/// </summary>
/// <remarks>
/// <para>
/// One catalog, three consumers: the Fake provider answers from it, Story 6.1's seeder builds its
/// Meetings from it, and Story 6.2's Evaluation Gate scores against it. There is no separate
/// golden directory and nothing here is copied into a second source of truth.
/// </para>
/// <para>
/// The index is the SHA-256 of the <em>normalized</em> notes body, not of the raw text, because
/// AD-21 says so and because the demo depends on it: an operator who pastes a case's notes out of
/// the repository picks up different wrapping and trailing whitespace than the file holds, and a
/// raw hash would miss where a normalized one matches. <c>MeetingNotes.Sha256</c> is a different
/// value — it hashes the raw text, to prove a run read the notes it claims to have read — and the
/// two must not be conflated.
/// </para>
/// <para>
/// <c>README.md</c> is excluded from the embedded glob by the project file, so this loader never
/// sees it. Every remaining <c>&lt;stem&gt;.md</c> must have a <c>&lt;stem&gt;.expected.json</c>
/// beside it.
/// </para>
/// </remarks>
public sealed partial class FixtureCatalog
{
    /// <summary>The logical-name prefix <c>ActionLedger.Infrastructure.csproj</c> gives catalog files.</summary>
    private const string Prefix = "fixtures/extraction/";

    private const string NotesExtension = ".md";

    private const string ExpectedSuffix = ".expected.json";

    private readonly IReadOnlyDictionary<string, FixtureCase> _byNormalizedHash;

    /// <summary>Loads the embedded catalog once.</summary>
    /// <exception cref="InvalidOperationException">A case has no answer file, or two cases normalize alike.</exception>
    public FixtureCatalog()
    {
        HashSet<string> names = [.. EmbeddedContent.NamesUnder(Prefix)];
        Dictionary<string, FixtureCase> byHash = [];
        List<FixtureCase> cases = [];

        RequireEveryAnswerFileHasItsCase(names);

        foreach (string name in names.Where(name => name.EndsWith(NotesExtension, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            string stem = name[Prefix.Length..^NotesExtension.Length];
            string expectedName = Prefix + stem + ExpectedSuffix;

            if (!names.Contains(expectedName))
            {
                throw new InvalidOperationException(
                    $"Fixture case '{stem}' has no '{stem}{ExpectedSuffix}' beside it. AD-21 pairs every case with its answer file.");
            }

            string body = NotesBody(stem, EmbeddedContent.Read(name));
            string key = NormalizedHashOf(body);

            FixtureCase fixture = new(stem, body, key, EmbeddedContent.Read(expectedName));

            if (!byHash.TryAdd(key, fixture))
            {
                throw new InvalidOperationException(
                    $"Fixture cases '{byHash[key].Stem}' and '{stem}' normalize to the same notes text, so the Fake "
                    + "provider cannot tell them apart. AD-21 keys the answer table by the normalized hash.");
            }

            cases.Add(fixture);
        }

        _byNormalizedHash = byHash;
        Cases = cases;
    }

    /// <summary>Every embedded case, ordered by stem.</summary>
    public IReadOnlyList<FixtureCase> Cases { get; }

    /// <summary>
    /// The case whose notes normalize the same way as <paramref name="notes"/>, or <c>null</c>.
    /// </summary>
    /// <param name="notes">Notes as they were pasted, wrapping and trailing whitespace and all.</param>
    public FixtureCase? Find(string notes) =>
        _byNormalizedHash.GetValueOrDefault(NormalizedHashOf(notes));

    /// <summary>
    /// The catalog's key for a piece of text: lower-case hex SHA-256 over the UTF-8 bytes of its
    /// FR-38 normalization. The idiom is <c>MeetingNotes</c>'; that one is <c>private</c> and hashes
    /// the raw text, so this repeats the idiom rather than reaching for it.
    /// </summary>
    /// <param name="text">Any text.</param>
    public static string NormalizedHashOf(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(TextNormalization.Normalize(text))));

    /// <summary>
    /// The pairing rule, in the direction the case loop cannot see. That loop walks
    /// <c>&lt;stem&gt;.md</c>, so an answer file whose notes file is missing or misspelled is not
    /// an error there — it is simply never reached, and the case vanishes from the catalog with
    /// nothing said. AD-21 pairs every case with its answer file in both directions.
    /// </summary>
    private static void RequireEveryAnswerFileHasItsCase(IReadOnlySet<string> names)
    {
        foreach (string name in names.Where(name => name.EndsWith(ExpectedSuffix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            string stem = name[Prefix.Length..^ExpectedSuffix.Length];

            if (!names.Contains(Prefix + stem + NotesExtension))
            {
                throw new InvalidOperationException(
                    $"Fixture answer file '{stem}{ExpectedSuffix}' has no '{stem}{NotesExtension}' beside it. "
                    + "AD-21 pairs every case with its answer file.");
            }
        }
    }

    /// <summary>
    /// The README's body rule, byte for byte: the notes are everything after the line containing
    /// the closing <c>---</c>, that line's terminating newline consumed, the remainder kept exactly.
    /// No trim, no re-wrap, no newline translation.
    /// </summary>
    /// <remarks>
    /// The pattern is <c>FixtureCatalogTests.SplitFrontMatter</c>'s. Trimming here would move where
    /// the body begins, every excerpt the answer files quote would still verify — they are interior
    /// sentences — but the normalized hash would change and the Fake would stop recognising its own
    /// catalog. <c>FakeProviderTests</c> is the test that catches it.
    /// </remarks>
    private static string NotesBody(string stem, string raw)
    {
        Match delimited = FrontMatter().Match(raw);

        return delimited.Success
            ? raw[delimited.Length..]
            : throw new InvalidOperationException(
                $"Fixture case '{stem}{NotesExtension}' does not open with '---' front matter closed by a '---' line. AD-21 requires it.");
    }

    [GeneratedRegex(@"\A---\r?\n(?<front>.*?)^---[ \t]*\r?\n", RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FrontMatter();
}

/// <summary>One catalog case: its notes body and the answer the Fake provider returns for it.</summary>
/// <param name="Stem">The kebab-case file stem, which is the case's key in the catalog.</param>
/// <param name="Notes">The notes body, byte-for-byte after the closing <c>---</c> line.</param>
/// <param name="NormalizedHash">Lower-case hex SHA-256 of the normalized <paramref name="Notes"/>.</param>
/// <param name="ExpectedJson">
/// The committed <c>&lt;stem&gt;.expected.json</c> text, unparsed. The Fake returns these bytes
/// rather than a re-serialization of them, so what the extractor validates is exactly what Story
/// 2.3 committed and Story 6.2 scores against.
/// </param>
public sealed record FixtureCase(string Stem, string Notes, string NormalizedHash, string ExpectedJson);
