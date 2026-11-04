using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Admin.Summary;

/// <summary>RouteGo's sales figures for a range, summed over every bus company: the five totals and one entry for every day of it.</summary>
public sealed record AdminSummary
{
    public required SalesTotals Totals { get; init; }

    public required IReadOnlyList<SalesDay> DailySeries { get; init; }
}

/// <summary>What the page shows: the figures, or why there are none; the checks on the typed dates sit beside the boxes.</summary>
public sealed record SummaryView
{
    public AdminSummary? Summary { get; init; }

    public PageAlert? Alert { get; init; }

    public ReportRangeCheck Check { get; init; } = new ReportRangeCheck();
}

public sealed class SummaryHandler
{
    private const string SummaryPath = "reports/admin/summary";

    private readonly IHttpClientFactory _httpClientFactory;

    public SummaryHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// The figures for a typed range. A range that fails the page's own checks is never sent;
    /// Reporting's own refusal of the dates is shown the same way.
    /// </summary>
    public async Task<SummaryView> LoadAsync(string? from, string? to, string correlationId, CancellationToken cancellationToken)
    {
        ReportRangeCheck check = ReportDisplay.CheckRange(from, to);
        if (check.Range is null)
        {
            return new SummaryView { Check = check };
        }

        string address = QueryHelpers.AddQueryString(SummaryPath, "from", ReportDisplay.WireDate(check.Range.From));
        address = QueryHelpers.AddQueryString(address, "to", ReportDisplay.WireDate(check.Range.To));
        HttpClient reporting = _httpClientFactory.CreateClient(HttpClients.Reporting);
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        HttpResponseMessage response = await reporting.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        ApiResult<AdminSummary> api = await response.ReadApiResultAsync<AdminSummary>(cancellationToken);
        if (api.IsSuccess)
        {
            return new SummaryView { Summary = api.Value, Check = check };
        }

        if (api.Errors != null)
        {
            return new SummaryView { Check = new ReportRangeCheck { RangeError = Messages.ValidationDateRange } };
        }

        return new SummaryView { Alert = ToAlert(api, correlationId), Check = check };
    }

    // Reporting answers only an admin; any refusal without words of its own is the shared one.
    private static PageAlert ToAlert(ApiResult<AdminSummary> api, string correlationId)
    {
        if (api.Problem is null)
        {
            return PageAlert.Danger(Messages.ProblemInternalError(correlationId));
        }

        return SharedProblems.ToAlert(api.Problem, correlationId);
    }
}
