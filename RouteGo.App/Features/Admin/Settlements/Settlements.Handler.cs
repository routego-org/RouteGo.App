using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Admin.Settlements;

/// <summary>One bus company's settlement for a business day, in rupees, as Reporting's nightly run wrote it.</summary>
public sealed record Settlement
{
    public required string OperatorId { get; init; }

    public required decimal Gross { get; init; }

    public required decimal Refunds { get; init; }

    /// <summary>Gross minus refunds; can be negative.</summary>
    public required decimal Net { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }
}

public sealed record SettlementList
{
    public required IReadOnlyList<Settlement> Items { get; init; }
}

/// <summary>A short-lived link that delivers one report; followed by a redirect, never shown, logged or kept.</summary>
public sealed record SettlementDownloadLink
{
    public required string Url { get; init; }
}

/// <summary>What the page shows for a typed day: the settlements, or why there are none.</summary>
public sealed record SettlementsView
{
    public IReadOnlyList<Settlement>? Items { get; init; }

    /// <summary>The day the settlements are for; set whenever they were asked for.</summary>
    public DateOnly? Day { get; init; }

    public PageAlert? Alert { get; init; }

    public string? DayError { get; init; }
}

/// <summary>What asking for a report came to: the address to send the browser to, or the words to show.</summary>
public sealed record DownloadOutcome
{
    public string? Address { get; init; }

    public PageAlert? Alert { get; init; }
}

public sealed class SettlementsHandler
{
    public const string ReportNotAvailable = "This settlement report is not available.";

    private readonly IHttpClientFactory _httpClientFactory;

    public SettlementsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>The settlements of a typed day. A day that is not a date is never sent.</summary>
    public async Task<SettlementsView> ListAsync(string? day, string correlationId, CancellationToken cancellationToken)
    {
        DateOnly? date = ReportDisplay.ParseDay(day);
        if (date is null)
        {
            return new SettlementsView { DayError = Messages.ValidationFormat("day") };
        }

        string address = QueryHelpers.AddQueryString("reports/settlements", "date", ReportDisplay.WireDate(date.Value));
        ApiResult<SettlementList> api = await GetAsync<SettlementList>(address, cancellationToken);
        if (api.IsSuccess)
        {
            return new SettlementsView { Items = api.Value!.Items, Day = date };
        }

        if (api.Errors != null)
        {
            return new SettlementsView { DayError = Messages.ValidationFormat("day") };
        }

        return new SettlementsView { Alert = ToAlert(api.Problem, correlationId) };
    }

    /// <summary>
    /// Asks Reporting for a fresh link to one company's report. The link works for a few minutes, is
    /// asked for again on every download, and goes only into the redirect that follows.
    /// </summary>
    public async Task<DownloadOutcome> DownloadAsync(DateOnly day, string operatorId, string correlationId, CancellationToken cancellationToken)
    {
        string address = $"reports/settlements/{ReportDisplay.WireDate(day)}/download";
        address = QueryHelpers.AddQueryString(address, "operatorId", operatorId);
        ApiResult<SettlementDownloadLink> api = await GetAsync<SettlementDownloadLink>(address, cancellationToken);
        if (api.IsSuccess)
        {
            return ToOutcome(api.Value!.Url);
        }

        bool isUnknown = api.Errors != null || (api.Problem != null && api.Problem.Code == BookingProblems.NotFound);
        if (isUnknown)
        {
            return new DownloadOutcome { Alert = PageAlert.Danger(ReportNotAvailable) };
        }

        return new DownloadOutcome { Alert = ToAlert(api.Problem, correlationId) };
    }

    /// <summary>"2 settlements for 29 Oct 2026." and "1 settlement for 29 Oct 2026.".</summary>
    public static string CountText(int count, DateOnly day)
    {
        if (count == 1)
        {
            return $"1 settlement for {IndiaTime.FormatDay(day)}.";
        }

        return $"{count} settlements for {IndiaTime.FormatDay(day)}.";
    }

    /// <summary>"No settlements for 30 Oct 2026. The nightly run may not have finished yet."</summary>
    public static string NoneText(DateOnly day)
    {
        return $"No settlements for {IndiaTime.FormatDay(day)}. The nightly run may not have finished yet.";
    }

    // A link that is not an ordinary web address is dropped, so the browser is never sent somewhere odd.
    private static DownloadOutcome ToOutcome(string link)
    {
        string? address = BookingDisplay.PaymentAddress(link);
        if (address is null)
        {
            return new DownloadOutcome { Alert = PageAlert.Danger(ReportNotAvailable) };
        }

        return new DownloadOutcome { Address = address };
    }

    private async Task<ApiResult<T>> GetAsync<T>(string address, CancellationToken cancellationToken)
    {
        HttpClient reporting = _httpClientFactory.CreateClient(HttpClients.Reporting);
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        HttpResponseMessage response = await reporting.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<T>(cancellationToken);
    }

    // Reporting answers only an admin; any refusal without words of its own is the shared one.
    private static PageAlert ToAlert(ApiProblem? problem, string correlationId)
    {
        if (problem is null)
        {
            return PageAlert.Danger(Messages.ProblemInternalError(correlationId));
        }

        return SharedProblems.ToAlert(problem, correlationId);
    }
}
