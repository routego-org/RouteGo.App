// Compiles into the TEST project (project setup's csproj links Common/Testing/**), so it carries
// every using it needs rather than relying on the app's GlobalUsings.cs.
using System.Net;
using System.Net.Http.Json;

namespace RouteGo.App.Common.Testing;

/// <summary>
/// Backend stubbing, solved once: replaces the "Backend" client's primary handler in tests, records
/// every request and its body, and answers from the routes a test maps. UI tests never hit a real
/// backend (references/host-setup.md).
/// </summary>
public sealed class FakeBackend : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

    /// <summary>The body of every request that carried one, in order — what a POST test asserts on.</summary>
    public List<string> RequestBodies { get; } = new List<string>();

    public string? LastRequestBody
    {
        get
        {
            if (RequestBodies.Count == 0)
            {
                return null;
            }
            return RequestBodies[RequestBodies.Count - 1];
        }
    }

    public string? LastRequestHeader(string name)
    {
        if (Requests.Count == 0)
        {
            return null;
        }
        HttpRequestMessage last = Requests[Requests.Count - 1];
        if (!last.Headers.TryGetValues(name, out IEnumerable<string>? values))
        {
            return null;
        }
        return values.FirstOrDefault();
    }

    private readonly List<(Func<HttpRequestMessage, bool> Match,
                           Func<HttpResponseMessage> Respond)> _routes =
        new List<(Func<HttpRequestMessage, bool>, Func<HttpResponseMessage>)>();

    /// <summary>Answers a method + absolute path, whatever the query string.</summary>
    public void Map(HttpMethod method, string path, Func<HttpResponseMessage> respond)
    {
        _routes.Add((request => request.Method == method
                                && request.RequestUri!.AbsolutePath == path, respond));
    }

    /// <summary>Answers a method + path AND query — for a search whose query string is the subject.</summary>
    public void MapPathAndQuery(HttpMethod method, string pathAndQuery, Func<HttpResponseMessage> respond)
    {
        _routes.Add((request => request.Method == method
                                && request.RequestUri!.PathAndQuery == pathAndQuery, respond));
    }

    public static HttpResponseMessage Json<T>(T body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
    }

    /// <summary>A page in the API's list shape: items plus a total count.</summary>
    public static HttpResponseMessage JsonPage<T>(IReadOnlyCollection<T> items, int? totalCount = null)
    {
        return Json(new { items, totalCount = totalCount ?? items.Count });
    }

    public static HttpResponseMessage ValidationError(string field, string message)
    {
        var errors = new Dictionary<string, string[]> { [field] = new[] { message } };
        return Json(new { errors }, HttpStatusCode.BadRequest);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (request.Content != null)
        {
            RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }
        foreach ((Func<HttpRequestMessage, bool> match, Func<HttpResponseMessage> respond) in _routes)
        {
            if (match(request))
            {
                return respond();
            }
        }
        return new HttpResponseMessage(HttpStatusCode.NotImplemented);
    }
}
