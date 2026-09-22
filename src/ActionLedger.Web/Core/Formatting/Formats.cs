using System.Globalization;

namespace ActionLedger.Web.Core.Formatting;

/// <summary>
/// UX-DR20 — the two date shapes every screen renders. Dates are <c>2026-10-03</c>, instants are
/// <c>2026-09-21 14:03 UTC</c>, and relative time is never used anywhere.
/// </summary>
/// <remarks>
/// <see cref="CultureInfo.InvariantCulture"/> is load-bearing rather than decoration. A WebAssembly
/// host carries the browser's culture, so <c>CurrentCulture</c> would render the same instant as
/// <c>21/09/2026</c> for one panelist and <c>2026/09/21</c> for another — and the Audit Trail is
/// evidence, which means one shape for everyone.
/// </remarks>
public static class Formats
{
    /// <summary>A due date, as <c>yyyy-MM-dd</c>.</summary>
    public static string Date(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// An instant, as <c>yyyy-MM-dd HH:mm UTC</c>. Converted to UTC first, so an offset the server
    /// or the browser happened to attach cannot change the rendered time.
    /// </summary>
    public static string Instant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>
    /// A live count against a limit, as <c>1,234 / 50,000</c>. The notes paste area is its only
    /// caller (EXPERIENCE.md, Notes paste area).
    /// </summary>
    /// <remarks>
    /// It is here rather than inline in the page for the same reason the two date shapes are: a
    /// WebAssembly host carries the browser's culture, and <c>N0</c> under de-DE renders the limit
    /// as <c>50.000</c>. The group separator is decoration, but a limit that reads differently per
    /// panelist is not.
    /// </remarks>
    public static string Count(int value, int limit) =>
        value.ToString("N0", CultureInfo.InvariantCulture)
        + " / "
        + limit.ToString("N0", CultureInfo.InvariantCulture);
}
