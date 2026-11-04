using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace RouteGo.App.Common.UI;

/// <summary>
/// Targets link cdn-href, script cdn-src and img cdn-src. Dev (CDN_BASE_URL unset): resolves ~/ to
/// the app-local path with the framework's file-version query, so a changed asset is never served
/// stale from the browser cache. Prod: prepends the CDN base, whose version segment does that job.
/// The same origin SecurityHeadersMiddleware allowlists, so the two never drift
/// (references/static-assets.md).
/// </summary>
[HtmlTargetElement("link", Attributes = "cdn-href")]
[HtmlTargetElement("script", Attributes = "cdn-src")]
[HtmlTargetElement("img", Attributes = "cdn-src")]
public sealed class CdnTagHelper : TagHelper
{
    private readonly IUrlHelperFactory _urlHelperFactory;
    private readonly IFileVersionProvider _fileVersionProvider;

    public CdnTagHelper(IUrlHelperFactory urlHelperFactory, IFileVersionProvider fileVersionProvider)
    {
        _urlHelperFactory = urlHelperFactory;
        _fileVersionProvider = fileVersionProvider;
    }

    [HtmlAttributeName("cdn-href")]
    public string? Href { get; set; }

    [HtmlAttributeName("cdn-src")]
    public string? Src { get; set; }

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        string? path = Href ?? Src;              // e.g. "~/App.styles.css"
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        string? cdn = CdnEnvironment.BaseUrl;          // unset in dev/test
        string? url;
        if (string.IsNullOrEmpty(cdn))
        {
            // dev: "~/x" -> "/x?v=<hash>" (honours PathBase)
            string local = _urlHelperFactory.GetUrlHelper(ViewContext).Content(path);
            url = _fileVersionProvider.AddFileVersionToPath(ViewContext.HttpContext.Request.PathBase, local);
        }
        else
        {
            url = $"{cdn.TrimEnd('/')}/{path.TrimStart('~', '/')}";    // prod: "https://.../v42/x"
        }

        string attribute = Href is not null ? "href" : "src";
        output.Attributes.SetAttribute(attribute, url);
        // the bound cdn-href/cdn-src attribute is stripped from the output automatically
    }
}
