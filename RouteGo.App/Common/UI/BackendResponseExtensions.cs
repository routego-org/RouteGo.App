using System.Net;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace RouteGo.App.Common.UI;

/// <summary>
/// Reads a backend response into an ApiResult and maps its errors onto ModelState, once, for every
/// slice. Validation failures become field errors, cataloged refusals a page-level message, and
/// anything 5xx throws — that is a bug surfacing on the error page, not a form state
/// (references/host-setup.md, "ApiResult").
/// </summary>
public static class BackendResponseExtensions
{
    // STJ on ValidationProblemDetails is unreliable (read-only Errors) — use local shapes.
    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors, string? Title);
    private sealed record ProblemBody(string? Code, string? Title, int? Status, string? Detail)
    {
        public string? TraceId { get; init; }

        // Everything the RFC 7807 core does not name: lockedUntil, accountStatus, retryAfterSeconds...
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extensions { get; init; }
    }

    // Every read goes through WireJson.Options — the ONE wire-format contract project setup stamps
    // (camelCase string enums, case-insensitive reads); the framework default would disagree with it.
    public static async Task<ApiResult<T>> ReadApiResultAsync<T>(
        this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            T? value = await response.Content.ReadFromJsonAsync<T>(WireJson.Options, cancellationToken);
            return ApiResult<T>.Success(value!);
        }

        // A refusal body may be parsed twice (as field errors, then as a problem); the content's
        // stream can be read only once, so the text is read once and parsed from memory.
        string text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
        {
            ValidationProblem? validation = TryParse<ValidationProblem>(text);
            if (validation is not null && validation.Errors is not null && validation.Errors.Count > 0)
            {
                return ApiResult<T>.Invalid(validation.Errors) with { ErrorsTitle = validation.Title };
            }
        }

        bool isProblemJson =
            response.Content.Headers.ContentType?.MediaType == "application/problem+json";
        // A problem body is a page state at every status: the portal's shared rules give
        // UPSTREAM_UNAVAILABLE / UPSTREAM_TIMEOUT and server errors their own in-page messages
        // (with the trace id) instead of the error page, so the person keeps what they typed.
        if (isProblemJson)
        {
            ProblemBody? body = TryParse<ProblemBody>(text);
            if (body is not null)
            {
                string title = string.IsNullOrEmpty(body.Title)
                    ? "The request could not be completed."
                    : body.Title;
                var problem = new ApiProblem(body.Code, title, (int)response.StatusCode)
                {
                    Detail = body.Detail,
                    TraceId = body.TraceId,
                    Extensions = body.Extensions ?? new Dictionary<string, JsonElement>(),
                };
                return ApiResult<T>.Refused(problem);
            }
        }

        // 5xx / unexpected: throw — that's a bug, not a form error. (EnsureSuccessStatusCode is a
        // banned API — it throws without the status code in a catchable, loggable shape.)
        throw new HttpRequestException(
            $"Backend call failed: {(int)response.StatusCode} {response.ReasonPhrase}",
            null, response.StatusCode);
    }

    // A body that is empty or not JSON reads as "no such shape", never as a crash.
    private static TBody? TryParse<TBody>(string text)
        where TBody : class
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TBody>(text, WireJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void AddToModelState<T>(this ApiResult<T> result,
        ModelStateDictionary modelState, string? prefix = null)
    {
        if (result.Errors is not null)
        {
            result.Errors.AddToModelState(modelState, prefix);
        }
        if (result.Problem is not null)
        {
            // The default fallback: unknown codes render the problem's title page-level.
            // Per-code messages come from the spec's screen states — switch on
            // result.Problem.Code first; never invent the wording here.
            modelState.AddModelError(string.Empty, result.Problem.Title);
        }
    }

    public static void AddToModelState(this IReadOnlyDictionary<string, string[]> errors,
        ModelStateDictionary modelState, string? prefix = null)
    {
        foreach ((string field, string[] messages) in errors)
        {
            string key = string.IsNullOrEmpty(prefix) ? field : $"{prefix}.{field}";
            foreach (string message in messages)
            {
                modelState.AddModelError(key, message);
            }
        }
    }
}
