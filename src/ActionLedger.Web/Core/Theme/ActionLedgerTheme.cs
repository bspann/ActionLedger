using MudBlazor;

namespace ActionLedger.Web.Core.Theme;

/// <summary>
/// The app's own <see cref="MudTheme"/>, which deliberately overrides nothing.
/// </summary>
/// <remarks>
/// DESIGN.md says the base palette is the MudTheme defaults, and that the six container-tier
/// values it pins are there only because MudBlazor has no container tier — they carry the
/// Material 3 azure-blue values, so the rendered result is unchanged. The whole design delta is
/// therefore <c>wwwroot/css/tokens.css</c> and the chips built on it, not a palette. This type
/// exists so <c>MudThemeProvider</c> is given an app-owned theme rather than an implicit default,
/// and so there is one named seam to change when a later story needs one.
/// </remarks>
public static class ActionLedgerTheme
{
    /// <summary>The single instance every <c>MudThemeProvider</c> in the app is bound to.</summary>
    public static MudTheme Instance { get; } = new();
}
