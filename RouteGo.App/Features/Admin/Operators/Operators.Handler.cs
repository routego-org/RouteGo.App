using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Admin.Operators;

/// <summary>An earlier application of the same operator that an admin rejected.</summary>
public sealed record EarlierRejection(DateTimeOffset SubmittedAt, DateTimeOffset RejectedAt, string Reason);

/// <summary>One operator's latest application as Identity lists it for review.</summary>
public sealed record ApplicationSummary
{
    /// <summary>The operator account the decision is sent for.</summary>
    public required Guid OperatorId { get; init; }

    public required string CompanyName { get; init; }

    public required string Gstin { get; init; }

    public required string ContactEmail { get; init; }

    /// <summary>The operator's sign-in email.</summary>
    public required string Email { get; init; }

    public required string FullName { get; init; }

    /// <summary>pendingVerification, pendingApproval, approved or rejected.</summary>
    public required string Status { get; init; }

    public required DateTimeOffset SubmittedAt { get; init; }

    public required DateTimeOffset? ReviewedAt { get; init; }

    public required string? RejectReason { get; init; }

    /// <summary>Newest first, as Identity orders them.</summary>
    public required IReadOnlyList<EarlierRejection> PreviousRejections { get; init; }
}

/// <summary>One page of applications, with how many match across all pages.</summary>
public sealed record ApplicationsPage(IReadOnlyList<ApplicationSummary> Items, int TotalCount, int Page);

/// <summary>Why the admin rejects the application; Notification emails it to the company as typed.</summary>
public sealed record RejectRequest(string Reason);

// The field of Identity's answer to a decision this page uses.
public sealed record ApplicationDecision(string CompanyName);

public sealed class OperatorsHandler
{
    /// <summary>How many applications one page of the list shows.</summary>
    public const int PageSize = 20;

    private const string ApplicationsPath = "operators/admin/applications";

    private readonly IHttpClientFactory _httpClientFactory;

    public OperatorsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>One page of operators' latest applications in a status, oldest submission first.</summary>
    public async Task<ApiResult<ApplicationsPage>> ListApplicationsAsync(OperatorsQuery query, CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        string address = QueryHelpers.AddQueryString(ApplicationsPath, ToSearchParameters(query));
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        HttpResponseMessage response = await identity.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<ApplicationsPage>(cancellationToken);
    }

    /// <summary>Approves the operator; it can sign in and sell, and Notification emails it.</summary>
    public async Task<ApiResult<ApplicationDecision>> ApproveAsync(Guid operatorId, CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{ApplicationsPath}/{operatorId}/approve");
        HttpResponseMessage response = await identity.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<ApplicationDecision>(cancellationToken);
    }

    /// <summary>Rejects the application with a reason; Notification emails the reason to the company.</summary>
    public async Task<ApiResult<ApplicationDecision>> RejectAsync(Guid operatorId, string reason, CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{ApplicationsPath}/{operatorId}/reject")
        {
            Content = JsonContent.Create(new RejectRequest(reason), options: WireJson.Options),
        };
        HttpResponseMessage response = await identity.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<ApplicationDecision>(cancellationToken);
    }

    // No status is sent for the default view: Identity's default is the review queue. The page
    // and page size always are.
    private static Dictionary<string, string?> ToSearchParameters(OperatorsQuery query)
    {
        var parameters = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(query.Status))
        {
            parameters["status"] = query.Status;
        }

        parameters["page"] = query.PageNumber.ToString(CultureInfo.InvariantCulture);
        parameters["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture);
        return parameters;
    }
}
