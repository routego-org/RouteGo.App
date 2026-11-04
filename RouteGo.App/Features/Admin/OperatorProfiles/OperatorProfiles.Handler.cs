using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Admin.OperatorProfiles;

/// <summary>
/// The search and page, kept in the page address so a reload or a shared link shows the same list
/// (?companyName=busco&amp;pageNumber=2).
/// </summary>
public sealed class OperatorProfilesQuery
{
    /// <summary>Text the company name must contain, ignoring capitals; empty means every company.</summary>
    public string? CompanyName { get; set; }

    /// <summary>The 1-based page of the list. Not named "page": Razor Pages reserves that route value for the page's own name.</summary>
    public int PageNumber { get; set; } = 1;

    public bool HasSearch()
    {
        return !string.IsNullOrEmpty(CompanyName);
    }
}

/// <summary>Fleet's record of one approved bus company.</summary>
public sealed record OperatorProfile
{
    /// <summary>Identity's operator account identifier; trip search filters by it.</summary>
    public required string OperatorId { get; init; }

    public required string CompanyName { get; init; }

    /// <summary>The operator's sign-in email.</summary>
    public required string Email { get; init; }

    /// <summary>The operator rating, 0.0 to 5.0 with one decimal place.</summary>
    public required decimal Rating { get; init; }

    public required DateTimeOffset ApprovedAt { get; init; }
}

/// <summary>One page of matching companies, with how many match across all pages.</summary>
public sealed record OperatorProfilesPage(IReadOnlyList<OperatorProfile> Items, int TotalCount, int Page);

public sealed class OperatorProfilesHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OperatorProfilesHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>One page of the companies whose name contains the search text, in Fleet's order (by company name).</summary>
    public async Task<ApiResult<OperatorProfilesPage>> SearchOperatorProfilesAsync(OperatorProfilesQuery query, CancellationToken cancellationToken)
    {
        HttpClient fleet = _httpClientFactory.CreateClient(HttpClients.Fleet);
        string address = QueryHelpers.AddQueryString("admin/operatorProfiles", ToParameters(query));
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        HttpResponseMessage response = await fleet.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<OperatorProfilesPage>(cancellationToken);
    }

    private static Dictionary<string, string?> ToParameters(OperatorProfilesQuery query)
    {
        var parameters = new Dictionary<string, string?>();
        if (query.HasSearch())
        {
            parameters["companyName"] = query.CompanyName;
        }

        parameters["page"] = query.PageNumber.ToString(CultureInfo.InvariantCulture);
        parameters["pageSize"] = FleetDisplay.ListPageSize.ToString(CultureInfo.InvariantCulture);
        return parameters;
    }
}
