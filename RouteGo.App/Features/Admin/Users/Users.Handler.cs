using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Admin.Users;

/// <summary>One account as Identity lists it for an admin.</summary>
public sealed record UserSummary
{
    public required Guid UserId { get; init; }

    public required string Email { get; init; }

    public required string FullName { get; init; }

    /// <summary>+91 followed by 10 digits, or null.</summary>
    public required string? Phone { get; init; }

    public required string Role { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>One page of matching accounts, with how many match across all pages.</summary>
public sealed record UsersPage(IReadOnlyList<UserSummary> Items, int TotalCount, int Page);

/// <summary>Why the admin suspends or reactivates the account.</summary>
public sealed record ReasonRequest(string Reason);

// The field of Identity's answer to a suspension or reactivation this page uses.
public sealed record AccountChange(string Email);

public sealed class UsersHandler
{
    /// <summary>How many accounts one page of the list shows.</summary>
    public const int PageSize = 20;

    private const string UsersPath = "auth/admin/users";

    private readonly IHttpClientFactory _httpClientFactory;

    public UsersHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>One page of the accounts that match the filters, newest first (Identity's default order).</summary>
    public async Task<ApiResult<UsersPage>> ListUsersAsync(UsersQuery query, CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        string address = QueryHelpers.AddQueryString(UsersPath, ToSearchParameters(query));
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        HttpResponseMessage response = await identity.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<UsersPage>(cancellationToken);
    }

    /// <summary>Suspends an account; Identity ends every session it has.</summary>
    public async Task<ApiResult<AccountChange>> SuspendAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        return await ChangeAsync(userId, "suspend", reason, cancellationToken);
    }

    /// <summary>Lets a suspended account sign in again.</summary>
    public async Task<ApiResult<AccountChange>> ReactivateAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        return await ChangeAsync(userId, "reactivate", reason, cancellationToken);
    }

    private async Task<ApiResult<AccountChange>> ChangeAsync(
        Guid userId, string action, string reason, CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{UsersPath}/{userId}/{action}")
        {
            Content = JsonContent.Create(new ReasonRequest(reason), options: WireJson.Options),
        };
        HttpResponseMessage response = await identity.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<AccountChange>(cancellationToken);
    }

    // Only the filters that are set are sent; the page and page size always are.
    private static Dictionary<string, string?> ToSearchParameters(UsersQuery query)
    {
        var parameters = new Dictionary<string, string?>();
        AddIfSet(parameters, "email", query.Email);
        AddIfSet(parameters, "role", query.Role);
        AddIfSet(parameters, "status", query.Status);
        parameters["page"] = query.PageNumber.ToString(CultureInfo.InvariantCulture);
        parameters["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture);
        return parameters;
    }

    private static void AddIfSet(Dictionary<string, string?> parameters, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        parameters[name] = value;
    }
}
