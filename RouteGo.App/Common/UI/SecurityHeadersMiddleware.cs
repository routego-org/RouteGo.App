using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace RouteGo.App.Common.UI;

/// <summary>
/// The security header set on every response, set in Response.OnStarting so it lands on HTML,
/// redirects and the error page alike. Generates the per-request CSP nonce ScriptNonceTagHelper
/// stamps on inline scripts, allowlists the CDN origin read through CdnEnvironment — never
/// appsettings — so the policy and the asset helper cannot drift, and the extra origins the app
/// declares in SecurityHeadersOptions (references/security.md).
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    public const string NonceKey = "csp-nonce";   // HttpContext.Items slot ScriptNonceTagHelper reads

    private readonly RequestDelegate _next;
    // Each is " origin origin" (leading space for concatenation) or "" — the CDN when set, plus
    // the app's own extra origins per directive.
    private readonly string _script;
    private readonly string _style;
    private readonly string _image;
    private readonly string _font;
    private readonly string _connect;
    private readonly string _formAction;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<SecurityHeadersOptions> options)
    {
        _next = next;
        string cdn = OriginOf(CdnEnvironment.BaseUrl);   // "" in dev/test -> 'self' only
        SecurityHeadersOptions extra = options.Value;
        _script = cdn + Joined(extra.ScriptSources);
        _style = cdn + Joined(extra.StyleSources);
        _image = cdn + Joined(extra.ImageSources);
        _font = cdn + Joined(extra.FontSources);
        _connect = Joined(extra.ConnectSources);
        _formAction = Joined(extra.FormActions);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[NonceKey] = nonce;  // ScriptNonceTagHelper stamps this onto every <script>

        context.Response.OnStarting(() =>
        {
            IHeaderDictionary headers = context.Response.Headers;
            headers["Content-Security-Policy"] =
                $"default-src 'self'; " +
                $"script-src 'self' 'nonce-{nonce}'{_script}; " +
                $"style-src 'self'{_style}; " +              // CSS isolation is an EXTERNAL bundle -> no 'unsafe-inline'
                $"img-src 'self' data:{_image}; " +
                $"font-src 'self'{_font}; " +
                $"connect-src 'self'{_connect}; " +
                "frame-ancestors 'none'; base-uri 'self'; " +
                $"form-action 'self'{_formAction}; object-src 'none'";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["X-Frame-Options"] = "DENY";           // belt-and-braces with frame-ancestors
            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=(), payment=()";
            return Task.CompletedTask;
        });

        await _next(context);
    }

    // "https://d123.cloudfront.net/v42" -> " https://d123.cloudfront.net" (leading space for concat); "" if unset
    private static string OriginOf(string? baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri))
        {
            return "";
        }
        return $" {uri.Scheme}://{uri.Authority}";
    }

    private static string Joined(List<string> origins)
    {
        if (origins.Count == 0)
        {
            return "";
        }
        return " " + string.Join(' ', origins);
    }
}
