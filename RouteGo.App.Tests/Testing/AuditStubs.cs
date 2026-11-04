namespace RouteGo.App.Tests.Testing;

/// <summary>Audit's answers for the audit log page: its AuditRecord and AuditRecordListResponse shapes.</summary>
public static class AuditStubs
{
    public const string AuditPath = "/audit";

    /// <summary>The address the portal sends for an item's records on a page.</summary>
    public static string ItemQuery(string entityType, string entityId, int page)
    {
        return $"{AuditPath}?entityType={entityType}&entityId={entityId}&page={page}&pageSize=20";
    }

    /// <summary>The address the portal sends for a day's records on a page.</summary>
    public static string DayQuery(string day, int page)
    {
        return $"{AuditPath}?date={day}&page={page}&pageSize=20";
    }

    /// <summary>Audit's AuditRecord; details are whatever the publisher wrote.</summary>
    public static object Record(string actorId, string actorRole, string action, string entityType, string entityId, string occurredAt, Dictionary<string, object>? details = null)
    {
        return new
        {
            eventId = Guid.NewGuid().ToString(),
            actorId,
            actorRole,
            action,
            entityType,
            entityId,
            occurredAt,
            details,
        };
    }

    /// <summary>Audit's AuditRecordListResponse.</summary>
    public static HttpResponseMessage Page(int page, int totalCount, params object[] items)
    {
        return FakeBackend.Json(new { items, totalCount, page, pageSize = 20 });
    }
}
