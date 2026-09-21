namespace ActionLedger.Web.Core.Shell;

/// <summary>
/// UX-DR18 — the one signal the shell's <c>MudProgressLinear</c> follows. Every in-flight request
/// brackets itself with <see cref="Begin"/> and <see cref="End"/>, so the bar is on whenever any
/// request is outstanding and off the moment the last one finishes. The body is never replaced by
/// skeleton rows.
/// </summary>
/// <remarks>
/// A counter rather than a flag, because two overlapping requests must not have the first one to
/// finish switch the bar off under the second. WebAssembly runs the UI on one thread, so a plain
/// field is enough and no synchronisation is needed.
/// </remarks>
public sealed class LoadingState
{
    private int inFlight;

    /// <summary>Raised whenever the count changes. The shell subscribes to it.</summary>
    public event Action? Changed;

    public bool IsBusy => inFlight > 0;

    /// <summary>Visible for tests, so "returns to zero" is assertable rather than inferred.</summary>
    public int InFlight => inFlight;

    public void Begin()
    {
        inFlight++;
        Changed?.Invoke();
    }

    /// <summary>
    /// Decrements, never below zero. Callers must invoke this from a <c>finally</c> — a thrown
    /// send would otherwise strand the bar on screen for the rest of the session.
    /// </summary>
    public void End()
    {
        if (inFlight > 0)
        {
            inFlight--;
        }

        Changed?.Invoke();
    }
}
