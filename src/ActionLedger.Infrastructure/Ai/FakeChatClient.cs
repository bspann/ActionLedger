using System.Text.Json;
using System.Text.RegularExpressions;
using ActionLedger.Application.Ai;
using ActionLedger.Infrastructure.Ai.Providers;
using Microsoft.Extensions.AI;

namespace ActionLedger.Infrastructure.Ai;

/// <summary>
/// AD-21 — the deterministic Fake provider. Answers from the embedded fixture catalog keyed by the
/// SHA-256 of the normalized notes, and falls back to the PRD's modal-verb heuristic.
/// </summary>
/// <remarks>
/// <para>
/// No I/O, no delay, no randomness: the same notes always produce the same answer, which is what
/// lets all of Sunday's work, CI, and the demo fallback run with no model server and no credential.
/// Usage is reported as zero rather than omitted, because FR-6 makes both token counts
/// non-nullable and Run Detail renders "0" rather than a blank.
/// </para>
/// <para>
/// On a catalog hit the committed <c>&lt;case&gt;.expected.json</c> bytes are returned unchanged
/// rather than a re-serialization of them, so what the extractor validates is exactly what Story
/// 2.3 committed and Story 6.2 scores against.
/// </para>
/// </remarks>
/// <param name="catalog">The embedded answer table.</param>
internal sealed partial class FakeChatClient(FixtureCatalog catalog) : IChatClient
{
    /// <summary>The notes member of the user message the extractor sends (the prompt's <c>## Input</c>).</summary>
    private const string NotesMember = "notes";

    /// <summary>PRD Glossary — at most five proposals from unknown notes.</summary>
    private const int MaxHeuristicProposals = 5;

    /// <summary>PRD Glossary — 0.85 for the first four, 0.55 for the fifth, so the low-confidence flag is demonstrable offline.</summary>
    private const double HighConfidence = 0.85;

    private const double LowConfidence = 0.55;

    /// <summary>
    /// Determiners that open a noun phrase. A capitalized run that starts with one is an article
    /// phrase, not a person — "The IT team" is not an owner, and a junk name on the Review Screen
    /// costs a reviewer more than the empty string the design prefers.
    /// </summary>
    private static readonly HashSet<string> Determiners =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "The", "A", "An", "This", "That", "These", "Those",
            "Every", "Each", "All", "Any", "No", "Some", "Both", "Either", "Neither",
            "Our", "Their", "Its", "His", "Her", "My", "Your",
        };

    /// <summary>
    /// The characters trimmed off a word before it is weighed as a name — quotes and brackets on
    /// the way in, sentence punctuation on the way out. "(Our vendor must invoice us.)" opens with
    /// a determiner just as plainly as "Our vendor" does, and the bracket must not hide it.
    /// </summary>
    private static readonly char[] WordEdges = ['"', '\'', '\u2018', '\u2019', '\u201c', '\u201d', '(', '[', '{', ',', ';', ':', '.', '!', '?'];

    private static readonly ChatClientMetadata Metadata =
        new(FakeChatClientFactory.ProviderName, providerUri: null, FakeChatClientFactory.ModelName);

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        cancellationToken.ThrowIfCancellationRequested();

        string notes = NotesFrom(messages);
        string answer = catalog.Find(notes)?.ExpectedJson ?? Heuristic(notes);

        ChatResponse response = new(new ChatMessage(ChatRole.Assistant, answer))
        {
            ModelId = FakeChatClientFactory.ModelName,
            FinishReason = ChatFinishReason.Stop,

            // FR-6 — zero, never null. The Fake makes no model call, so there is nothing to count,
            // and a run that recorded "unknown" would be a run Run Detail could not render.
            Usage = new UsageDetails { InputTokenCount = 0, OutputTokenCount = 0 },
        };

        return Task.FromResult(response);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Streaming is not part of the seam: AD-11's extractor makes one buffered call per attempt and
    /// validates the whole document, so there is nothing a partial response could be validated
    /// against. This satisfies the interface by streaming the one buffered answer.
    /// </remarks>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatResponse response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        foreach (ChatResponseUpdate update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is not null)
        {
            return null;
        }

        return serviceType == typeof(ChatClientMetadata) ? Metadata
            : serviceType.IsInstanceOfType(this) ? this
            : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to release: the catalog is a singleton this client borrows, and there is no
        // socket, no handler and no file behind a Fake that answers from memory.
    }

    /// <summary>
    /// The notes out of the last user message. The extractor sends
    /// <c>{"meetingDate": "...", "notes": "..."}</c> (the prompt's own <c>## Input</c> contract); a
    /// message that is not that object is taken as the notes themselves, so a caller that hands the
    /// Fake plain text still gets an answer rather than an exception.
    /// </summary>
    private static string NotesFrom(IEnumerable<ChatMessage> messages)
    {
        string text = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text ?? string.Empty;

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(NotesMember, out JsonElement notes)
                && notes.ValueKind == JsonValueKind.String)
            {
                return notes.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Not the extractor's envelope. Fall through and treat the message as the notes.
        }

        return text;
    }

    /// <summary>
    /// PRD Glossary — one proposal per sentence containing <c>will</c>, <c>should</c>,
    /// <c>needs to</c> or <c>must</c>, up to five, in sentence order: that sentence verbatim as the
    /// excerpt, the first capitalized name in it as the owner, no due date, and 0.85 for the first
    /// four with 0.55 for the fifth.
    /// </summary>
    private static string Heuristic(string notes)
    {
        List<ExtractedAction> actions = [];

        foreach (string sentence in Sentences(notes))
        {
            if (actions.Count == MaxHeuristicProposals)
            {
                break;
            }

            // A sentence too long to cite is skipped rather than truncated: an excerpt is only an
            // excerpt if it is verbatim, and FR-5 makes over-length a failure, never a trim.
            if (sentence.Length > ExtractionOutputValidator.SourceExcerptMaxLength || !ModalVerb().IsMatch(sentence))
            {
                continue;
            }

            actions.Add(new ExtractedAction
            {
                Description = Shortened(sentence),
                SuggestedOwner = OwnerIn(sentence),
                SuggestedDueDate = null,
                Confidence = actions.Count < MaxHeuristicProposals - 1 ? HighConfidence : LowConfidence,
                SourceExcerpt = sentence,
            });
        }

        return JsonSerializer.Serialize(
            new ExtractionOutput { Actions = actions },
            ExtractionOutput.SerializerOptions);
    }

    /// <summary>
    /// The notes as sentences, in order, each trimmed of the whitespace around it but otherwise
    /// verbatim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A line break ends a sentence, exactly as a full stop does.</strong> Pasted notes are
    /// mostly lists, and a bullet list rarely punctuates its items: matching across newlines made
    /// "Actions agreed:" and three unpunctuated bullets one proposal quoting the whole block —
    /// and, once that block passed 1,000 characters, no proposal at all, because an excerpt that
    /// long cannot be cited. One line is at most one sentence, and usually exactly one commitment.
    /// </para>
    /// <para>
    /// A terminator ends a sentence only when whitespace or the end of the line follows it.
    /// Splitting on every full stop cut "Dana will order 2.5 tons of salt." after "2.", and the
    /// fragment still verified as an excerpt because it is a genuine substring of the notes — so a
    /// truncated half-sentence reached the Review Screen looking like a real citation. The same
    /// rule keeps "e.g." and an email address inside the sentence that contains them.
    /// </para>
    /// </remarks>
    private static IEnumerable<string> Sentences(string notes)
    {
        foreach (string line in LineBreak().Split(notes))
        {
            foreach (Match sentence in SentenceSpan().Matches(line))
            {
                string text = sentence.Value.Trim();

                if (text.Length > 0)
                {
                    yield return text;
                }
            }
        }
    }

    /// <summary>
    /// <paramref name="sentence"/> as a description, cut to the schema's bound on a rune boundary.
    /// </summary>
    /// <remarks>
    /// A plain <c>sentence[..500]</c> cuts at a UTF-16 index, so a sentence carrying an emoji or
    /// any other astral character across the bound would leave a lone surrogate in the description
    /// — a string that is not valid text, headed for a database column and a browser.
    /// </remarks>
    private static string Shortened(string sentence)
    {
        if (sentence.Length <= ExtractionOutputValidator.DescriptionMaxLength)
        {
            return sentence;
        }

        int cut = ExtractionOutputValidator.DescriptionMaxLength;

        return sentence[..(char.IsHighSurrogate(sentence[cut - 1]) ? cut - 1 : cut)];
    }

    /// <summary>
    /// The first capitalized word sequence in the sentence, or the empty string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sequence that is <em>nothing but</em> the sentence's own first word is skipped, because
    /// every sentence starts with a capital and "The printer must be replaced." names nobody. A
    /// leading full name survives that test — "Dana Whitfield will …" yields
    /// <c>Dana Whitfield</c> — and the fixture corpus writes every commitment that way, with the
    /// roster's two-word display names.
    /// </para>
    /// <para>
    /// The cost of that rule is a single-word leading name: "Marcus needs to call the vendor."
    /// yields the empty string rather than <c>Marcus</c>, because nothing distinguishes it from
    /// "The". This is the unknown-notes fallback for text that matches no catalog case, where an
    /// empty owner is a blank a reviewer fills in and a wrong owner is a blank they first have to
    /// notice; no acceptance criterion pins the owner on this path.
    /// </para>
    /// <para>
    /// <strong>A rejected leading word ends the search.</strong> Scanning on would make
    /// "Dana will email Marcus Chen." yield <c>Marcus Chen</c> — the object of the sentence, the
    /// person the work is owed <em>to</em> — which is the one answer worse than the empty string,
    /// because a reviewer reads it as settled rather than as missing. The same reasoning applies
    /// to a determiner-led run, which is skipped whole for the run that follows it inside the same
    /// noun phrase ("The IT team" must not yield <c>IT</c>).
    /// </para>
    /// <para>
    /// The result is never longer than the schema allows: a run past 100 characters is not a name,
    /// and the Fake must not emit output its own validator would reject.
    /// </para>
    /// </remarks>
    private static string OwnerIn(string sentence)
    {
        string[] words = sentence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        for (int start = 0; start < words.Length; start++)
        {
            if (!IsCapitalized(words[start]))
            {
                continue;
            }

            int end = start;

            while (end + 1 < words.Length && IsCapitalized(words[end + 1]))
            {
                end++;
            }

            if (start == 0 && end == 0)
            {
                // The sentence's own first word, capitalized only because it starts a sentence.
                // Stop, rather than scan on: the next capitalized run in "Dana will email Marcus
                // Chen." is the object, and naming the person the work is owed to is a worse
                // answer than naming nobody.
                return string.Empty;
            }

            if (Determiners.Contains(words[start].Trim(WordEdges)))
            {
                // "The IT team must patch the server." — a determiner opens a noun phrase, never a
                // name. Skip the whole run, not just its first word, or "IT" would be taken next.
                start = end;

                continue;
            }

            string owner = string.Join(' ', words[start..(end + 1)]).TrimEnd(',', ';', ':');

            if (end == words.Length - 1)
            {
                owner = owner.TrimEnd('.', '!', '?');
            }

            return owner.Length is > 0 and <= ExtractionOutputValidator.SuggestedOwnerMaxLength ? owner : string.Empty;
        }

        return string.Empty;
    }

    /// <summary>A word counts as capitalized when its first letter is an upper-case letter.</summary>
    private static bool IsCapitalized(string word)
    {
        foreach (char character in word)
        {
            if (char.IsLetter(character))
            {
                return char.IsUpper(character);
            }
        }

        return false;
    }

    [GeneratedRegex(@"\b(will|should|must|needs\s+to)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModalVerb();

    [GeneratedRegex(@"\r?\n", RegexOptions.CultureInvariant)]
    private static partial Regex LineBreak();

    /// <summary>
    /// One sentence: any run of text, then the terminators that end it and any closing bracket or
    /// quote after them. A terminator that is <em>not</em> followed by whitespace, a closer or the
    /// end of the line is interior punctuation and is consumed rather than treated as an ending —
    /// which is what keeps "2.5", "e.g." and an email address inside their own sentence.
    /// </summary>
    [GeneratedRegex(
        @"(?:[^.!?]|[.!?](?![\s)\]}\u0022\u0027\u2019\u201d]|$))+[.!?]*[)\]}\u0022\u0027\u2019\u201d]*",
        RegexOptions.CultureInvariant)]
    private static partial Regex SentenceSpan();
}
