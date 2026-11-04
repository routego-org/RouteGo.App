using Microsoft.AspNetCore.Razor.TagHelpers;

namespace RouteGo.App.Common.UI;

/// <summary>
/// Under the strict CSP an inline script runs only with the request nonce. This stamps it on every
/// script tag, so no slice hand-threads it. Needs AddHttpContextAccessor() (references/security.md).
/// </summary>
[HtmlTargetElement("script")]
public sealed class ScriptNonceTagHelper : TagHelper
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ScriptNonceTagHelper(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (_httpContextAccessor.HttpContext?.Items[SecurityHeadersMiddleware.NonceKey] is string nonce)
        {
            output.Attributes.SetAttribute("nonce", nonce);
        }
    }
}
