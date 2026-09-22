using ActionLedger.Application.Ai;

namespace ActionLedger.Application.Abstractions;

/// <summary>
/// AD-11 — the one AI port. Turns a Meeting's notes into validated proposals, and never throws for
/// a provider or validation failure.
/// </summary>
/// <remarks>
/// <para>
/// Every type this port names is provider-neutral. <c>DependencyRuleTests</c> Rule 3 bans
/// <c>Microsoft.Extensions.AI</c> and <c>OpenAI</c> from this ring at the project file and at the
/// assembly, so the SDK that does the talking stops one ring out and swapping it is Story 2.7's
/// single-folder diff.
/// </para>
/// <para>
/// Infrastructure has exactly one implementation, <c>ChatClientActionExtractor</c>, and
/// <c>AiSeamTests</c> fails the build on a second: a second extraction path is a path that can skip
/// validation.
/// </para>
/// </remarks>
public interface IActionExtractor
{
    /// <summary>
    /// Runs one extraction: at most two provider calls, strict deserialization, validation, then
    /// excerpt verification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This method does not throw for a failed run.</strong> An invalid response, a provider
    /// exception, or a per-call timeout is a failed attempt: the extractor calls once more, and if
    /// that also fails it returns <see cref="ExtractionResult.Failed"/> with a reason naming what
    /// went wrong. Story 2.5 persists that result as a Failed run and the controller answers 201,
    /// because a model that returned nonsense is not an HTTP error.
    /// </para>
    /// <para>
    /// The one exception that does escape is a cancellation of
    /// <paramref name="cancellationToken"/>. That is the caller abandoning the request, not a run
    /// that failed, and recording it as a Failed run would invent a run nobody waited for.
    /// </para>
    /// </remarks>
    /// <param name="request">The notes and the Meeting date, and nothing else about the Meeting.</param>
    /// <param name="cancellationToken">The caller's token. Cancelling it propagates.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ExtractionResult> ExtractAsync(ExtractionRequest request, CancellationToken cancellationToken = default);
}
