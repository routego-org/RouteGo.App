using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Admin.AuditLog;

public sealed class AuditLogModel : PageModel
{
    private const string KindKey = "kind";
    private const string IdKey = "id";
    private const string DayKey = "day";
    private const string PageKey = "page";
    private const string AuditLogAddress = "/admin/audit-log";

    private readonly AuditLogHandler _handler;
    private readonly IClock _clock;

    public AuditLogModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new AuditLogHandler(httpClientFactory);
        _clock = clock;
    }

    /// <summary>The Kind chosen for an item lookup, or the first kind offered.</summary>
    public string Kind { get; private set; } = string.Empty;

    public string ItemId { get; private set; } = string.Empty;

    /// <summary>The Day box as typed, or today when the address names none.</summary>
    public string DayText { get; private set; } = string.Empty;

    public int PageNumber { get; private set; } = 1;

    /// <summary>Whether an item lookup, a day lookup or neither is in the address.</summary>
    public bool IsItemLookup { get; private set; }

    public bool IsDayLookup { get; private set; }

    public AuditView View { get; private set; } = new AuditView();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ReadAddress();
        string correlationId = CorrelationId.Get(HttpContext);
        if (IsItemLookup)
        {
            View = await _handler.LookUpItemAsync(Kind, ItemId, PageNumber, correlationId, cancellationToken);
            return;
        }

        if (IsDayLookup)
        {
            View = await _handler.LookUpDayAsync(DayText, PageNumber, correlationId, cancellationToken);
        }
    }

    /// <summary>The address of one page of the lookup on show, so a link or a reload shows the same records.</summary>
    public string PageAddress(int page)
    {
        var parameters = new Dictionary<string, string?>();
        if (IsItemLookup)
        {
            parameters[KindKey] = Kind;
            parameters[IdKey] = ItemId;
        }
        else
        {
            parameters[DayKey] = DayText;
        }

        if (page > 1)
        {
            parameters[PageKey] = page.ToString(CultureInfo.InvariantCulture);
        }

        return QueryHelpers.AddQueryString(AuditLogAddress, parameters);
    }

    // Item wins when the address names both; a day or an item named with nothing in the box is told so.
    private void ReadAddress()
    {
        IQueryCollection query = Request.Query;
        Kind = Text(query, KindKey);
        if (Kind.Length == 0)
        {
            Kind = AuditLogHandler.Kinds[0];
        }

        ItemId = Text(query, IdKey);
        IsItemLookup = query.ContainsKey(KindKey) || query.ContainsKey(IdKey);
        IsDayLookup = !IsItemLookup && query.ContainsKey(DayKey);
        DayText = ReportDisplay.WireDate(IndiaTime.Today(_clock.UtcNow));
        if (query.ContainsKey(DayKey))
        {
            DayText = Text(query, DayKey);
        }

        bool isPage = int.TryParse(Text(query, PageKey), NumberStyles.None, CultureInfo.InvariantCulture, out int page);
        if (isPage && page >= 1)
        {
            PageNumber = page;
        }
    }

    private static string Text(IQueryCollection query, string key)
    {
        string? typed = query[key];
        return (typed ?? string.Empty).Trim();
    }
}
