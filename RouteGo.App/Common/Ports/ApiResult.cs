namespace RouteGo.App.Common.Ports;

/// <summary>
/// A business refusal from the backend: the cataloged code (contracts/problem-types.md), the
/// problem's title, and the status. A page switches on Code where the spec names a message and
/// falls back to Title for every code it does not know (references/host-setup.md, "ApiResult").
/// </summary>
public sealed record ApiProblem(string? Code, string Title, int Status)
{
    /// <summary>The problem's human-readable detail, when the backend sent one; never parsed, only shown.</summary>
    public string? Detail { get; init; }

    /// <summary>The backend's trace id, quoted to support in the internal-error message.</summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// The problem's extension members a page needs for its wording — for example lockedUntil,
    /// accountStatus or retryAfterSeconds. Empty when the backend sent none.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Extensions { get; init; } =
        new Dictionary<string, JsonElement>();

    /// <summary>An extension member as text, or null when the problem does not carry it.</summary>
    public string? GetExtension(string name)
    {
        if (!Extensions.TryGetValue(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return value.GetRawText();
    }
}

/// <summary>
/// How every backend answer reaches a page: a value, field errors (400/422), or a refusal
/// (409/404/410 problem+json). 5xx never arrives here — BackendResponseExtensions throws instead.
/// </summary>
public sealed record ApiResult<T>(
    T? Value, IReadOnlyDictionary<string, string[]>? Errors, ApiProblem? Problem)
{
    public bool IsSuccess
    {
        get { return Errors is null && Problem is null; }
    }

    /// <summary>
    /// The title of the validation problem that carried <see cref="Errors"/> — what a page shows at
    /// the top when the refused field is not one it shows. Null when the answer had no title.
    /// </summary>
    public string? ErrorsTitle { get; init; }

    public static ApiResult<T> Success(T value)
    {
        return new ApiResult<T>(value, null, null);
    }

    public static ApiResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors)
    {
        return new ApiResult<T>(default, errors, null);
    }

    public static ApiResult<T> Refused(ApiProblem problem)
    {
        return new ApiResult<T>(default, null, problem);
    }
}
