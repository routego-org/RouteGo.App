namespace RouteGo.App.Common.UI;

/// <summary>
/// A page-level message shown at the top of the content (Features/Shared/_PageAlert.cshtml), with
/// optional links to the next steps — "Sign in", "Send the link again", or both "Re-apply" and
/// "Sign in" when the person may need either.
/// </summary>
public sealed record PageAlert(string Text, string Kind)
{
    /// <summary>The next-step links, in the order they are shown.</summary>
    public IReadOnlyList<Link> Links { get; init; } = Array.Empty<Link>();

    /// <summary>A danger alert: something was refused or failed.</summary>
    public static PageAlert Danger(string text)
    {
        return new PageAlert(text, Flash.KindDanger);
    }

    /// <summary>A warning alert: waiting or not yet possible.</summary>
    public static PageAlert Warning(string text)
    {
        return new PageAlert(text, Flash.KindWarning);
    }

    /// <summary>An information alert.</summary>
    public static PageAlert Info(string text)
    {
        return new PageAlert(text, Flash.KindInfo);
    }

    /// <summary>A success alert.</summary>
    public static PageAlert Success(string text)
    {
        return new PageAlert(text, Flash.KindSuccess);
    }

    /// <summary>The same alert with one more next-step link after the ones it already has.</summary>
    public PageAlert WithLink(string text, string href)
    {
        var links = new List<Link>(Links);
        links.Add(new Link(text, href));
        return this with { Links = links };
    }

    /// <summary>A next-step link inside the alert, such as "Sign in" to /auth/login.</summary>
    public sealed record Link(string Text, string Href);
}
