using System.Globalization;
using System.Text.Json;
using ActionLedger.Application.Abstractions;
using ActionLedger.Application.Ai;
using ActionLedger.Infrastructure.Ai.Providers;
using Microsoft.Extensions.AI;

namespace ActionLedger.Infrastructure.Ai;

/// <summary>
/// AD-11 — the one <see cref="IActionExtractor"/>. Everything provider-specific stops at
/// <see cref="IChatClientFactory"/>; this class talks only to <see cref="IChatClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// The shape of a run: build the request from the catalog prompt and the committed schema, call the
/// provider under the per-call budget, deserialize strictly, validate, and only then verify
/// excerpts. An invalid response, a provider exception, or a timeout is a failed attempt — call
/// once more, and if that also fails return <see cref="ExtractionResult.Failed"/>. At most two
/// calls, so a run stays inside NFR-1's 180-second ceiling.
/// </para>
/// <para>
/// <strong>Nothing here throws for a failed run.</strong> The one exception that escapes is a
/// cancellation of the caller's own token, which is an abandoned request rather than a run that
/// failed, and recording it as Failed would invent a run nobody waited for.
/// </para>
/// <para>
/// NFR-4 — the notes text is never logged and never put into a failure reason. A reason names what
/// went wrong (which member, which index, which exception type), not what the meeting said.
/// </para>
/// </remarks>
public sealed class ChatClientActionExtractor : IActionExtractor
{
    /// <summary>
    /// The strict-structured-output flag <c>Microsoft.Extensions.AI</c>'s OpenAI adapter reads out
    /// of <c>ChatOptions.AdditionalProperties</c> (AD-11).
    /// </summary>
    /// <remarks>
    /// <para>
    /// What was checked, and how: <c>Microsoft.Extensions.AI.OpenAI</c> is the adapter that reads
    /// this flag, and it declares the key as the private constant
    /// <c>Microsoft.Extensions.AI.OpenAIClientExtensions.StrictKey</c>, whose literal value is
    /// <c>"strict"</c> and whose value type is <c>bool</c>. That was read out of the adapter
    /// assembly's own metadata, not from memory.
    /// </para>
    /// <para>
    /// <strong>No project in this solution references that package</strong> — real providers are
    /// Story 2.7's — so nothing here builds against the constant and no test in this story can
    /// catch a wrong key. Story 2.7 must re-confirm this key against the adapter version it pins
    /// before trusting it, because the first real request is where a wrong one would surface.
    /// </para>
    /// </remarks>
    internal const string StrictSchemaKey = "strict";

    /// <summary>The schema name a structured-output request carries, for the provider's own error messages.</summary>
    private const string SchemaName = "extract_actions";

    private readonly IChatClient _chatClient;
    private readonly IPromptCatalog _prompts;
    private readonly IChatClientFactory _provider;
    private readonly AiSettings _settings;
    private readonly TimeProvider _time;

    /// <summary>Wires the extractor to the active provider.</summary>
    /// <param name="chatClient">The client the active factory built.</param>
    /// <param name="prompts">The embedded prompt catalog (AD-6).</param>
    /// <param name="provider">The active factory, for the provider and model names on the metrics.</param>
    /// <param name="settings">The validated <c>Ai</c> values, handed in by the composition root.</param>
    /// <param name="time">
    /// The clock the per-call budget and <c>DurationMs</c> are measured against. <c>TimeProvider</c>
    /// rather than <c>IClock</c>: a test needs to move a timeout forward without waiting for it, and
    /// <c>IClock</c> (AD-15) answers "what time is it" for domain timestamps, not "wake me in 90
    /// seconds".
    /// </param>
    public ChatClientActionExtractor(
        IChatClient chatClient,
        IPromptCatalog prompts,
        IChatClientFactory provider,
        AiSettings settings,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _chatClient = chatClient;
        _prompts = prompts;
        _provider = provider;
        _settings = settings;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<ExtractionResult> ExtractAsync(
        ExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string promptVersion = _prompts.Current;
        ChatMessage[] messages =
        [
            new(ChatRole.System, _prompts.Get(promptVersion)),
            new(ChatRole.User, UserMessage(request)),
        ];

        ChatOptions options = new()
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                ExtractionSchema.WireJson,
                SchemaName,
                "The commitments the meeting notes record."),

            // AD-11 — strict mode. The adapter transforms the schema before sending (all properties
            // required, additionalProperties false, unsupported keywords demoted to descriptions),
            // so the wire schema stays a strict subset of the committed file.
            AdditionalProperties = new AdditionalPropertiesDictionary { [StrictSchemaKey] = true },
        };

        DateTimeOffset startedAt = _time.GetUtcNow();
        long startedTicks = _time.GetTimestamp();
        TimeSpan budget = TimeSpan.FromSeconds(_settings.CallTimeoutSeconds);

        // Accumulated as long and clamped once, at the end. Clamping each attempt to int.MaxValue
        // and then adding as int is how two large attempts overflow to a negative total — the
        // opposite of what clamping is for.
        long inputTokens = 0;
        long outputTokens = 0;
        string failure = "The provider was never called.";

        // AD-11 — one retry, then Failed. Two calls at the 90-second budget is 180 seconds, which is
        // the run ceiling exactly; a third would break it.
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            // Checked here, not only inside the call: a client that does not honour its token
            // would otherwise let an abandoned request spend a second attempt and come back
            // Succeeded, which is the run nobody waited for that the remarks above rule out.
            cancellationToken.ThrowIfCancellationRequested();

            ChatResponse? response;

            try
            {
                response = await CallAsync(messages, options, budget, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller abandoned the request. Deliberately not converted to Failed (AD-11).
                throw;
            }
            catch (OperationCanceledException)
            {
                failure = $"The provider did not answer within Ai:CallTimeoutSeconds ({_settings.CallTimeoutSeconds}s) on attempt {attempt} of 2.";

                continue;
            }
            catch (Exception exception)
            {
                // Every provider failure is a failed attempt, by AD-11 — a refused connection, a
                // 500, a truncated body. Nothing escapes this method as an exception.
                failure = $"The provider failed on attempt {attempt} of 2: {exception.GetType().Name}: {exception.Message}";

                continue;
            }

            // Tokens are summed over every attempt, including the ones whose answer was rejected:
            // a retried run cost what both calls cost, and a run that under-reported would make the
            // Evaluation Gate's cost column a fiction.
            inputTokens += NonNegative(response.Usage?.InputTokenCount);
            outputTokens += NonNegative(response.Usage?.OutputTokenCount);

            ExtractionOutput? output;

            try
            {
                // Layer one — strict deserialization: required members, unmapped members disallowed.
                output = JsonSerializer.Deserialize<ExtractionOutput>(response.Text, ExtractionOutput.SerializerOptions);
            }
            catch (JsonException exception)
            {
                failure = $"The provider's response did not match the extraction schema on attempt {attempt} of 2: {exception.Message}";

                continue;
            }

            if (output is null)
            {
                failure = $"The provider returned no JSON object on attempt {attempt} of 2.";

                continue;
            }

            // Layer two — lengths, ranges and date format, all or nothing (FR-5).
            if (ExtractionOutputValidator.Validate(output) is { } invalid)
            {
                failure = $"{invalid} (attempt {attempt} of 2)";

                continue;
            }

            return Verify(
                output,
                request.Notes,
                Metrics(promptVersion, startedAt, startedTicks, inputTokens, outputTokens));
        }

        return ExtractionResult.Failed(
            failure,
            Metrics(promptVersion, startedAt, startedTicks, inputTokens, outputTokens));
    }

    /// <summary>
    /// One provider call under the per-call budget, on a token linked to the caller's so a caller
    /// who gives up cancels the call in flight.
    /// </summary>
    private async Task<ChatResponse> CallAsync(
        ChatMessage[] messages,
        ChatOptions options,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        // Two sources rather than CancelAfter, because the budget's timer has to come from
        // TimeProvider: linking keeps the caller's cancellation distinguishable from the timeout,
        // which is the difference between an abandoned request and a failed attempt.
        using CancellationTokenSource timeout = new(budget, _time);
        using CancellationTokenSource attempt =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        return await _chatClient.GetResponseAsync(messages, options, attempt.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Layer three — FR-5's post-validation filter. A proposal whose excerpt is not in the notes is
    /// dropped with a warning; the run still succeeds, and both halves travel in the result so
    /// Story 6.2 never re-filters.
    /// </summary>
    private static ExtractionResult Verify(ExtractionOutput output, string notes, ExtractionMetrics metrics)
    {
        List<ExtractedProposal> kept = [];
        List<DroppedProposal> dropped = [];
        List<string> warnings = [];

        foreach (ExtractedAction action in output.Actions)
        {
            ExtractedProposal proposal = new(
                action.Description,
                action.SuggestedOwner,
                action.SuggestedDueDate is { } dueDate
                    ? DateOnly.ParseExact(dueDate, ExtractionOutputValidator.DueDateFormat, CultureInfo.InvariantCulture)
                    : null,
                action.Confidence,
                action.SourceExcerpt);

            if (ExcerptVerifier.IsSubstring(action.SourceExcerpt, notes))
            {
                kept.Add(proposal);

                continue;
            }

            string warning =
                $"Dropped a proposal whose source excerpt is not in the notes: \"{action.SourceExcerpt}\"";

            dropped.Add(new DroppedProposal(proposal, warning));
            warnings.Add(warning);
        }

        return ExtractionResult.Succeeded(kept, dropped, metrics, warnings);
    }

    /// <summary>
    /// The user message the prompt's <c>## Input</c> section declares: the two values it names, and
    /// nothing else about the Meeting (FR-4).
    /// </summary>
    private static string UserMessage(ExtractionRequest request) =>
        JsonSerializer.Serialize(
            new Dictionary<string, string>
            {
                ["meetingDate"] = request.MeetingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["notes"] = request.Notes,
            },
            ExtractionOutput.SerializerOptions);

    private ExtractionMetrics Metrics(
        string promptVersion,
        DateTimeOffset startedAt,
        long startedTicks,
        long inputTokens,
        long outputTokens) =>
        new(
            _settings.Provider,
            _provider.Model,
            promptVersion,
            ExtractionSchema.Version,
            startedAt,
            (int)Math.Min(
                int.MaxValue,
                Math.Round(_time.GetElapsedTime(startedTicks).TotalMilliseconds, MidpointRounding.AwayFromZero)),
            Clamp(inputTokens),
            Clamp(outputTokens));

    /// <summary>
    /// One attempt's token count as a non-negative <c>long</c>. A provider that reports nothing
    /// contributes nothing rather than making the run unreportable.
    /// </summary>
    private static long NonNegative(long? tokens) => tokens is null or < 0 ? 0 : tokens.Value;

    /// <summary>
    /// The accumulated total as the <c>int</c> FR-6 makes it — clamped once, over the sum, so two
    /// implausibly large attempts saturate rather than wrap to a negative.
    /// </summary>
    private static int Clamp(long tokens) => (int)Math.Clamp(tokens, 0, int.MaxValue);
}
