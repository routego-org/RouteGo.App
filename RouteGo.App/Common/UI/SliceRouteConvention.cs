using System.Text;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace RouteGo.App.Common.UI;

/// <summary>
/// With RootDirectory = "/Features", the page Features/Orders/PlaceOrder/PlaceOrder.cshtml would
/// route to /Orders/PlaceOrder/PlaceOrder. This convention collapses the doubled final segment and
/// kebab-cases it: /orders/place-order. A @page template suffix ("{id:guid}") is preserved.
/// A page that declares an ABSOLUTE route (@page "/invite-accept") keeps it: it declared one
/// because something outside the app already fixed its URL (references/host-setup.md).
/// </summary>
public sealed class SliceRouteConvention : IPageRouteModelConvention
{
    public void Apply(PageRouteModel model)
    {
        // ViewEnginePath: "/Orders/PlaceOrder/PlaceOrder" (no extension, rooted at RootDirectory)
        string path = model.ViewEnginePath.Trim('/');
        string[] segments = path.Split('/');
        if (segments.Length < 2 || segments[^1] != segments[^2])
        {
            return;   // not the doubled slice shape
        }

        // drop the doubled last segment, kebab-case the rest: "orders/place-order"
        string[] kebabSegments = new string[segments.Length - 1];
        for (int i = 0; i < kebabSegments.Length; i++)
        {
            kebabSegments[i] = Kebab(segments[i]);
        }
        string collapsed = string.Join('/', kebabSegments);
        foreach (SelectorModel selector in model.Selectors)
        {
            string template = selector.AttributeRouteModel!.Template ?? "";
            if (!IsDerivedFrom(template, path))
            {
                continue;   // the page declared its own URL — not ours to rewrite
            }

            // template == path, or path + "/{id:guid}" when @page declares a suffix
            string suffix = template[path.Length..];
            selector.AttributeRouteModel.Template = collapsed + suffix;
        }
    }

    /// <summary>
    /// Whether a route template is the one the file's location produced, rather than one the
    /// page declared for itself with an absolute @page. Only the former is ours to rewrite.
    /// </summary>
    private static bool IsDerivedFrom(string template, string path)
    {
        if (template == path)
        {
            return true;
        }

        return template.StartsWith(path + "/", StringComparison.Ordinal);
    }

    private static string Kebab(string pascal)   // "PlaceOrder" -> "place-order"
    {
        var result = new StringBuilder();
        for (int i = 0; i < pascal.Length; i++)
        {
            if (char.IsUpper(pascal[i]) && i > 0)
            {
                result.Append('-');
            }
            result.Append(char.ToLowerInvariant(pascal[i]));
        }
        return result.ToString();
    }
}
