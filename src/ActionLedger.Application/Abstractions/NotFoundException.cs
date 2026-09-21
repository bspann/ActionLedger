namespace ActionLedger.Application.Abstractions;

/// <summary>
/// A resource named by the request does not exist. The Api maps this to HTTP 404 with
/// ProblemDetails <c>type: not-found</c> (AD-13 Errors row).
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string resource, object id)
        : base($"{resource} '{id}' was not found.")
    {
        Resource = resource;
        Id = id;
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The resource kind, when the caller supplied one. Never surfaced in the response body.</summary>
    public string? Resource { get; }

    /// <summary>The identifier that missed, when the caller supplied one.</summary>
    public object? Id { get; }
}
