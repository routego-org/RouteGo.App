namespace RouteGo.App.Common.UI;

/// <summary>The five sales figures for a period, in rupees, as Reporting counts them.</summary>
public sealed record SalesTotals
{
    /// <summary>Bookings confirmed in the period, including any cancelled later.</summary>
    public required int ConfirmedBookings { get; init; }

    public required int ConfirmedSeats { get; init; }

    /// <summary>The amounts of the bookings confirmed in the period.</summary>
    public required decimal Revenue { get; init; }

    /// <summary>The refunds completed in the period.</summary>
    public required decimal Refunds { get; init; }

    /// <summary>Revenue minus refunds; can be negative.</summary>
    public required decimal Net { get; init; }
}

/// <summary>One business day's sales figures.</summary>
public sealed record SalesDay
{
    public required DateOnly Date { get; init; }

    public required int ConfirmedBookings { get; init; }

    public required int ConfirmedSeats { get; init; }

    public required decimal Revenue { get; init; }

    public required decimal Refunds { get; init; }

    public required decimal Net { get; init; }
}

/// <summary>A range of business days, both ends included, that passed the page's own checks.</summary>
public sealed record ReportRange(DateOnly From, DateOnly To);

/// <summary>
/// What checking a typed From and To found: the range when both are allowed, otherwise the words
/// to show next to the fields. A page sends nothing to Reporting unless <see cref="Range"/> is set.
/// </summary>
public sealed record ReportRangeCheck
{
    public ReportRange? Range { get; init; }

    public string? FromError { get; init; }

    public string? ToError { get; init; }

    /// <summary>The reversed or too-long range message; shown beside both fields.</summary>
    public string? RangeError { get; init; }
}
