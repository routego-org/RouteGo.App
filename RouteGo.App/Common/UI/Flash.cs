namespace RouteGo.App.Common.UI;

/// <summary>
/// The TempData keys a PageModel sets before RedirectToPage and Features/Shared/_Flash.cshtml
/// renders once. Spelled here so no slice carries the magic strings (references/host-setup.md).
/// </summary>
public static class Flash
{
    /// <summary>The message.</summary>
    public const string Key = "Flash";

    /// <summary>The variant; unset reads as <see cref="KindSuccess"/>, so only the others need setting.</summary>
    public const string KindKey = "FlashKind";

    public const string KindSuccess = "success";
    public const string KindWarning = "warning";
    public const string KindInfo = "info";
    public const string KindDanger = "danger";
}
