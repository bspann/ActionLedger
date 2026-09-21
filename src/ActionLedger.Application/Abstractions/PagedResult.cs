namespace ActionLedger.Application.Abstractions;

/// <summary>
/// The envelope every list endpoint returns: <c>{ items, page, pageSize, total }</c>
/// (Consistency Conventions, Paging row).
/// </summary>
/// <remarks>
/// <para>
/// No endpoint returns one yet — the first arrives with Story 1.4's user roster. The shape is
/// defined and published as an OpenAPI component now so the contract, and the client generated
/// from it, carry the envelope from the first commit rather than growing it later.
/// </para>
/// <para>
/// <see cref="Page"/> is 1-based. <see cref="Total"/> is the count across all pages, not the
/// length of <see cref="Items"/>.
/// </para>
/// </remarks>
/// <typeparam name="T">The DTO carried by this page.</typeparam>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    /// <summary>An empty page, echoing the paging window the caller asked for.</summary>
    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}

/// <summary>
/// The paging window a list endpoint accepts. Defaults and bounds live here so every list
/// applies the same ones (Consistency Conventions, Paging row).
/// </summary>
public static class Paging
{
    /// <summary>The lowest page number; paging is 1-based.</summary>
    public const int FirstPage = 1;

    /// <summary>The page size used when the caller does not ask for one.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The largest page size a caller may ask for.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Clamps a requested window into the permitted range.</summary>
    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(page ?? FirstPage, FirstPage),
         Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
