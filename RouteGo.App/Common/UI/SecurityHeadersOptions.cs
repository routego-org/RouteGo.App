namespace RouteGo.App.Common.UI;

/// <summary>
/// The origins an app allowlists in its CSP beyond 'self' and the CDN. Set in Program.cs's services
/// seam (<c>Configure&lt;SecurityHeadersOptions&gt;</c>), so SecurityHeadersMiddleware never needs
/// editing by hand (references/security.md). The stamped defaults are the RouteGo design system's own
/// needs — every app had added them by hand: its stylesheets load Google Fonts, and its
/// the country-select component loads flag images from the platform's countries CDN. An app ADDS to the
/// lists (an S3 bucket for pre-signed uploads, a sibling app a form posts to); it clears one only
/// when it has replaced the design system.
/// </summary>
public sealed class SecurityHeadersOptions
{
    /// <summary>Where the design system's fonts come from.</summary>
    public const string FontStylesheetOrigin = "https://fonts.googleapis.com";
    public const string FontFileOrigin = "https://fonts.gstatic.com";

    /// <summary>The platform's countries CDN, which the country-select component loads its flag images from.
    /// Deliberately not a connect-src entry: it serves no CORS headers, so the browser could never
    /// read it; an app that needs the data reads it server-side.</summary>
    public const string FlagImageOrigin = "https://d2pxtnyp5e658h.cloudfront.net";

    /// <summary>Extra script-src origins. Prefer the CDN; an inline script needs the nonce, not an entry here.</summary>
    public List<string> ScriptSources { get; } = new List<string>();

    /// <summary>Extra style-src origins.</summary>
    public List<string> StyleSources { get; } = new List<string> { FontStylesheetOrigin };

    /// <summary>Extra img-src origins.</summary>
    public List<string> ImageSources { get; } = new List<string> { FlagImageOrigin };

    /// <summary>Extra font-src origins.</summary>
    public List<string> FontSources { get; } = new List<string> { FontFileOrigin };

    /// <summary>Extra connect-src origins — a bucket the browser uploads to on a pre-signed link.</summary>
    public List<string> ConnectSources { get; } = new List<string>();

    /// <summary>Extra form-action origins — the login app, or a sibling app a form posts straight to.</summary>
    public List<string> FormActions { get; } = new List<string>();
}
