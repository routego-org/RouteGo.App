using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Admin.AuditLog;

/// <summary>One permanent note of an audited action, exactly as Audit wrote it.</summary>
public sealed record AuditRecord
{
    public required string ActorId { get; init; }

    /// <summary>The role the actor acted in; Customer, Operator, Admin or another such as System.</summary>
    public required string ActorRole { get; init; }

    public required string Action { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The reason and figures the publisher gave, as written; null or empty when there are none.</summary>
    public Dictionary<string, JsonElement>? Details { get; init; }
}

/// <summary>One page of records, oldest first, with how many match across all pages.</summary>
public sealed record AuditRecordList
{
    public required IReadOnlyList<AuditRecord> Items { get; init; }

    public required int TotalCount { get; init; }
}

/// <summary>What the page shows for a lookup: the records, or why there are none; field messages sit beside their boxes.</summary>
public sealed record AuditView
{
    public AuditRecordList? Records { get; init; }

    public PageAlert? Alert { get; init; }

    public string? KindError { get; init; }

    public string? IdError { get; init; }

    public string? DayError { get; init; }
}

public sealed class AuditLogHandler
{
    /// <summary>How many records one page shows.</summary>
    public const int PageSize = 20;

    public const string NoRecords = "No records found.";
    public const string ReadOnlyNote = "Records cannot be changed or removed.";

    private const string AuditPath = "audit";
    private const string BookingKind = "Booking";
    private const string TripKind = "Trip";

    private readonly IHttpClientFactory _httpClientFactory;

    public AuditLogHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>The kinds the page offers; Audit keeps any kind it is sent, so a new kind is a new entry here.</summary>
    public static IReadOnlyList<string> Kinds { get; } = new string[] { BookingKind, TripKind, "Payment", "Operator", "Account", "SigningKey" };

    /// <summary>Everything done to one item. A kind that is not offered or an empty Item ID is never sent.</summary>
    public async Task<AuditView> LookUpItemAsync(string kind, string itemId, int page, string correlationId, CancellationToken cancellationToken)
    {
        bool isKind = Kinds.Contains(kind);
        string id = itemId.Trim();
        if (!isKind || id.Length == 0)
        {
            return new AuditView { KindError = KindErrorFor(isKind), IdError = IdErrorFor(id) };
        }

        var parameters = new Dictionary<string, string?> { ["entityType"] = kind, ["entityId"] = id };
        return await SearchAsync(parameters, page, correlationId, cancellationToken);
    }

    /// <summary>Everything done on one business day. A day that is not a date is never sent.</summary>
    public async Task<AuditView> LookUpDayAsync(string? day, int page, string correlationId, CancellationToken cancellationToken)
    {
        DateOnly? date = ReportDisplay.ParseDay(day);
        if (date is null)
        {
            return new AuditView { DayError = Messages.ValidationFormat("day") };
        }

        var parameters = new Dictionary<string, string?> { ["date"] = ReportDisplay.WireDate(date.Value) };
        return await SearchAsync(parameters, page, correlationId, cancellationToken);
    }

    /// <summary>"2 records." and "1 record.".</summary>
    public static string CountText(int total)
    {
        if (total == 1)
        {
            return "1 record.";
        }

        return $"{total} records.";
    }

    /// <summary>"C-1, Customer": the actor, and the role in the portal's words when it is one of the three, as given otherwise.</summary>
    public static string ActorText(AuditRecord record)
    {
        return $"{record.ActorId}, {RoleText(record.ActorRole)}";
    }

    /// <summary>"Booking B-501".</summary>
    public static string ItemText(AuditRecord record)
    {
        return $"{record.EntityType} {record.EntityId}";
    }

    /// <summary>Where an item's own page is: a booking's page or a trip's seats; null for any other kind.</summary>
    public static string? ItemAddress(AuditRecord record)
    {
        string id = Uri.EscapeDataString(record.EntityId);
        if (record.EntityType == BookingKind)
        {
            return $"/admin/bookings/{id}";
        }

        if (record.EntityType == TripKind)
        {
            return $"/admin/trips/{id}/seats";
        }

        return null;
    }

    /// <summary>The details, one "key: value" line each, exactly as the publisher wrote them.</summary>
    public static IReadOnlyList<string> DetailLines(AuditRecord record)
    {
        var lines = new List<string>();
        if (record.Details is null)
        {
            return lines;
        }

        foreach (KeyValuePair<string, JsonElement> detail in record.Details)
        {
            lines.Add($"{detail.Key}: {ValueText(detail.Value)}");
        }

        return lines;
    }

    /// <summary>How many pages the matching records fill.</summary>
    public static int PageCount(int total)
    {
        return (total + PageSize - 1) / PageSize;
    }

    private async Task<AuditView> SearchAsync(Dictionary<string, string?> parameters, int page, string correlationId, CancellationToken cancellationToken)
    {
        parameters["page"] = page.ToString(CultureInfo.InvariantCulture);
        parameters["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture);
        string address = QueryHelpers.AddQueryString(AuditPath, parameters);
        HttpClient audit = _httpClientFactory.CreateClient(HttpClients.Audit);
        var request = new HttpRequestMessage(HttpMethod.Get, address);
        HttpResponseMessage response = await audit.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        ApiResult<AuditRecordList> api = await response.ReadApiResultAsync<AuditRecordList>(cancellationToken);
        if (api.IsSuccess)
        {
            return new AuditView { Records = api.Value };
        }

        if (api.Errors != null)
        {
            return ToFieldErrors(api.Errors);
        }

        return new AuditView { Alert = ToAlert(api.Problem, correlationId) };
    }

    // Audit's refusals name entityType, entityId or date; each shows under its own box.
    private static AuditView ToFieldErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        string? kindError = null;
        string? idError = null;
        string? dayError = null;
        if (errors.ContainsKey("entityType"))
        {
            kindError = Messages.ValidationFormat("kind");
        }

        if (errors.ContainsKey("entityId"))
        {
            idError = Messages.ValidationRequired("Item ID");
        }

        if (errors.ContainsKey("date"))
        {
            dayError = Messages.ValidationFormat("day");
        }

        return new AuditView { KindError = kindError, IdError = idError, DayError = dayError };
    }

    private static string? KindErrorFor(bool isKind)
    {
        if (isKind)
        {
            return null;
        }

        return Messages.ValidationFormat("kind");
    }

    private static string? IdErrorFor(string id)
    {
        if (id.Length > 0)
        {
            return null;
        }

        return Messages.ValidationRequired("Item ID");
    }

    private static string RoleText(string role)
    {
        bool isKnown = role == PortalSession.RoleCustomer || role == PortalSession.RoleOperator || role == PortalSession.RoleAdmin;
        if (!isKnown)
        {
            return role;
        }

        return Messages.RoleLabel(role);
    }

    private static string ValueText(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return value.GetRawText();
    }

    // Audit answers only an admin; any refusal without words of its own is the shared one.
    private static PageAlert ToAlert(ApiProblem? problem, string correlationId)
    {
        if (problem is null)
        {
            return PageAlert.Danger(Messages.ProblemInternalError(correlationId));
        }

        return SharedProblems.ToAlert(problem, correlationId);
    }
}
