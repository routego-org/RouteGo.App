namespace RouteGo.App.Tests.Testing;

/// <summary>
/// Reporting's answers for the report pages: sales figures as its SalesTotals and SalesDay, an
/// operator's summary and the occupancy of the operator's trips on a day. Net is always revenue
/// minus refunds, as Reporting works it out.
/// </summary>
public static class ReportStubs
{
    public const string OperatorSummaryPath = "/reports/operator/summary";
    public const string OperatorTripsPath = "/reports/operator/trips";

    public const string DashboardAddress = "/operator/dashboard";

    /// <summary>The dashboard address the page settles on for the test clock (2 Oct 2026, IST): the last 7 days and today.</summary>
    public const string DefaultDashboardAddress = "/operator/dashboard?from=2026-09-26&to=2026-10-02&day=2026-10-02";

    /// <summary>Reporting's SalesTotals.</summary>
    public static object Totals(int bookings, int seats, decimal revenue, decimal refunds)
    {
        return new { confirmedBookings = bookings, confirmedSeats = seats, revenue, refunds, net = revenue - refunds };
    }

    /// <summary>Reporting's SalesDay.</summary>
    public static object Day(string date, int bookings, int seats, decimal revenue, decimal refunds)
    {
        return new { date, confirmedBookings = bookings, confirmedSeats = seats, revenue, refunds, net = revenue - refunds };
    }

    /// <summary>Reporting's OperatorSummary for the range, with the totals and days given.</summary>
    public static HttpResponseMessage Summary(string from, string to, object totals, params object[] days)
    {
        return FakeBackend.Json(new { from, to, currency = "INR", totals, dailySeries = days });
    }

    /// <summary>Reporting's TripOccupancy.</summary>
    public static object Trip(string tripId, string source, string destination, string departureAt, int seatsSold, int seatsTotal)
    {
        return new { tripId, source, destination, departureAt, seatsSold, seatsTotal };
    }

    /// <summary>Reporting's OperatorTripListResponse.</summary>
    public static HttpResponseMessage Trips(params object[] items)
    {
        return FakeBackend.Json(new { items, totalCount = items.Length });
    }

    /// <summary>The summary address the portal sends for the range.</summary>
    public static string SummaryQuery(string from, string to)
    {
        return $"{OperatorSummaryPath}?from={from}&to={to}";
    }

    /// <summary>The trips address the portal sends for the day.</summary>
    public static string TripsQuery(string day)
    {
        return $"{OperatorTripsPath}?date={day}";
    }

    public const string AdminSummaryPath = "/reports/admin/summary";
    public const string SettlementsPath = "/reports/settlements";

    /// <summary>The admin summary address the portal sends for the range.</summary>
    public static string AdminSummaryQuery(string from, string to)
    {
        return $"{AdminSummaryPath}?from={from}&to={to}";
    }

    /// <summary>The settlements address the portal sends for the day.</summary>
    public static string SettlementsQuery(string day)
    {
        return $"{SettlementsPath}?date={day}";
    }

    /// <summary>The download address the portal sends for a company and day.</summary>
    public static string DownloadQuery(string day, string operatorId)
    {
        return $"{SettlementsPath}/{day}/download?operatorId={operatorId}";
    }

    /// <summary>Reporting's Settlement; net is gross minus refunds.</summary>
    public static object Settlement(string date, string operatorId, decimal gross, decimal refunds)
    {
        return new
        {
            settlementDate = date,
            operatorId,
            gross,
            refunds,
            net = gross - refunds,
            currency = "INR",
            generatedAt = "2026-10-29T18:35:00Z",
        };
    }

    /// <summary>Reporting's SettlementListResponse.</summary>
    public static HttpResponseMessage Settlements(params object[] items)
    {
        return FakeBackend.Json(new { items, totalCount = items.Length });
    }

    /// <summary>Reporting's SettlementDownloadLink.</summary>
    public static HttpResponseMessage DownloadLink(string url)
    {
        return FakeBackend.Json(new { url, expiresAt = "2026-10-02T05:35:00Z" });
    }

    /// <summary>How many requests reached Reporting with the path (whatever the query).</summary>
    public static int Calls(FakeBackend reporting, string path)
    {
        return reporting.Requests.Count(request => request.RequestUri!.AbsolutePath == path);
    }
}
