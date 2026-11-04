// Compiles into the TEST project (project setup's csproj links Common/Testing/**), so it carries
// every using it needs rather than relying on the app's GlobalUsings.cs.
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace RouteGo.App.Common.Testing;

/// <summary>
/// The antiforgery dance, solved once: GET the page, find the form, POST it with the token the
/// server rendered. The antiforgery cookie travels on its own (references/host-setup.md).
/// </summary>
public sealed class FormClient
{
    private const int BodySnippetLength = 2000;

    private static readonly IBrowsingContext _browser =
        BrowsingContext.New(AngleSharp.Configuration.Default);

    private readonly HttpClient _client;   // from TestHost.CreateClient: https base, no redirects

    public FormClient(HttpClient client)
    {
        _client = client;
    }

    /// <summary>GETs a page and parses it. A failure quotes the start of the body, so a 500 reads
    /// as the exception page's text rather than a bare status code.</summary>
    public async Task<IDocument> GetPageAsync(string url)
    {
        HttpResponseMessage response = await _client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            string snippet = body.Length <= BodySnippetLength ? body : body[..BodySnippetLength];
            throw new InvalidOperationException(
                $"GET {url} returned {(int)response.StatusCode}. Response body: {snippet}");
        }
        var absolute = new Uri(_client.BaseAddress!, url);
        return await ReadDocumentAsync(response, absolute.ToString());
    }

    /// <summary>Parses a response already in hand — the 200 re-render after an invalid POST.</summary>
    public async Task<IDocument> ReadDocumentAsync(HttpResponseMessage response, string? address = null)
    {
        string html = await response.Content.ReadAsStringAsync();
        return await _browser.OpenAsync(request =>
        {
            request.Content(html);
            if (!string.IsNullOrEmpty(address))
            {
                request.Address(address);
            }
        });
    }

    public Task<HttpResponseMessage> SendFormAsync(
        IDocument doc, string formSelector, Dictionary<string, string> fields)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach ((string name, string value) in fields)
        {
            pairs.Add(new KeyValuePair<string, string>(name, value));
        }
        return SendFormAsync(doc, formSelector, pairs);
    }

    /// <summary>The list overload sends a field more than once — a multi-select, a checkbox group.</summary>
    public async Task<HttpResponseMessage> SendFormAsync(
        IDocument doc, string formSelector, IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        var form = doc.QuerySelector(formSelector) as IHtmlFormElement;
        if (form is null)
        {
            throw new InvalidOperationException($"no form: {formSelector}");
        }
        string action = doc.Url;
        if (!string.IsNullOrEmpty(form.Action))
        {
            action = form.Action;
        }
        return await PostFormAsync(form, action, fields);
    }

    /// <summary>Submits through a button with its own formaction — a secondary action on a form.</summary>
    public Task<HttpResponseMessage> SendButtonAsync(
        IDocument doc, string buttonSelector, Dictionary<string, string> fields)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach ((string name, string value) in fields)
        {
            pairs.Add(new KeyValuePair<string, string>(name, value));
        }
        return SendButtonAsync(doc, buttonSelector, pairs);
    }

    /// <summary>The list overload sends a field more than once — a checkbox group.</summary>
    public async Task<HttpResponseMessage> SendButtonAsync(
        IDocument doc, string buttonSelector, IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        var button = doc.QuerySelector(buttonSelector) as IHtmlButtonElement;
        if (button is null)
        {
            throw new InvalidOperationException($"no button: {buttonSelector}");
        }
        var form = button.Form as IHtmlFormElement;
        if (form is null)
        {
            throw new InvalidOperationException($"button outside a form: {buttonSelector}");
        }
        string? action = button.FormAction;
        if (string.IsNullOrEmpty(action))
        {
            throw new InvalidOperationException($"button has no formaction: {buttonSelector}");
        }

        return await PostFormAsync(form, action, fields);
    }

    private async Task<HttpResponseMessage> PostFormAsync(
        IHtmlFormElement form, string action, IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        var tokenInput = form.QuerySelector("input[name=__RequestVerificationToken]") as IHtmlInputElement;
        if (tokenInput is null)
        {
            throw new InvalidOperationException("no antiforgery token in form");
        }
        string token = tokenInput.Value;

        var payload = new List<KeyValuePair<string, string>>(fields);
        payload.Add(new KeyValuePair<string, string>("__RequestVerificationToken", token));
        return await _client.PostAsync(action, new FormUrlEncodedContent(payload));
    }
}
