namespace ActionLedger.Domain.Webhooks;

/// <summary>
/// Where one outbox message stands in delivery (AD-8). Stored as its name (AD-10).
/// </summary>
public enum OutboxState
{
    /// <summary>Written by the approval's commit and not yet delivered. The dispatcher's work queue.</summary>
    Pending,

    /// <summary>The receiver answered 2xx.</summary>
    Delivered,

    /// <summary>Every attempt failed; the dispatcher gave up.</summary>
    Dead,
}
