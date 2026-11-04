using System.Globalization;

namespace RouteGo.App.Common.UI;

/// <summary>A seat class's fare in whole rupees, as Fleet sends and takes it.</summary>
public sealed record ClassFare(string SeatClass, int Fare);

/// <summary>The travel dates a trip list covers, both included.</summary>
public sealed record TripDates(DateOnly From, DateOnly To);

/// <summary>
/// The rules the schedule and trip pages share, written once: how days, departure times and fares
/// are shown, how a schedule is named, how a fare is read from a box, and the alerts for the
/// refusals several of these pages meet.
/// </summary>
public static class TripDisplay
{
    public const int MinFare = 50;
    public const int MaxFare = 10000;

    public const string UnknownRoute = "Unknown route";
    public const string UnknownBus = "Unknown bus";
    public const string OpenBusText = "Open the bus";

    public const string BusInactive = "BUS_INACTIVE";
    public const string BusTripOverlap = "BUS_TRIP_OVERLAP";
    public const string ScheduleInactive = "SCHEDULE_INACTIVE";
    public const string ClassFaresMismatch = "CLASS_FARES_MISMATCH";

    private const string EveryDay = "Every day";
    private const string AllStatuses = "All";
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>How many days after the first date a trip list covers when no last date is given (two weeks in all).</summary>
    private const int DefaultListDays = 13;

    /// <summary>The most days one trip list may cover, both ends included.</summary>
    private const int MaxListDays = 31;

    /// <summary>Fleet's days of the week, Monday first — the order days are always shown in.</summary>
    public static readonly string[] WeekDays = new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };

    /// <summary>A trip list's status filter choices in the order offered: "All" (empty), then Fleet's trip statuses.</summary>
    public static readonly string[] TripStatusChoices = new[] { "", "scheduled", "departed", "cancelled" };

    /// <summary>
    /// The travel dates a trip list covers: as typed (yyyy-MM-dd), or today through today + 13 days
    /// for a date left empty — Fleet's own default. Null when a date cannot be read, To is before
    /// From, or the range covers more than 31 days.
    /// </summary>
    public static TripDates? ListDates(string? from, string? to, DateOnly today)
    {
        DateOnly? first = DateOrDefault(from, today);
        if (first is null)
        {
            return null;
        }

        DateOnly? last = DateOrDefault(to, first.Value.AddDays(DefaultListDays));
        if (last is null || last.Value < first.Value)
        {
            return null;
        }

        if (last.Value.DayNumber - first.Value.DayNumber + 1 > MaxListDays)
        {
            return null;
        }

        return new TripDates(first.Value, last.Value);
    }

    /// <summary>A date as Fleet takes it in an address: yyyy-MM-dd.</summary>
    public static string WireDate(DateOnly date)
    {
        return date.ToString(DateFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>The status filter's word for a trip status: "All" for none, otherwise the catalog's state word.</summary>
    public static string TripStatusChoice(string status)
    {
        if (status.Length == 0)
        {
            return AllStatuses;
        }

        return TripStatusLabel(status);
    }

    /// <summary>When a trip arrives: the time alone on its travel date, the date too when it arrives on a later day.</summary>
    public static string Arrives(DateOnly serviceDate, DateTimeOffset arrivalAt)
    {
        DateOnly arrivalDay = IndiaTime.Today(arrivalAt);
        if (arrivalDay == serviceDate)
        {
            return IndiaTime.FormatTime(arrivalAt);
        }

        return IndiaTime.Format(arrivalAt);
    }

    /// <summary>"Monday, Wednesday, Friday" in week order; all seven days are "Every day".</summary>
    public static string DaysText(IReadOnlyCollection<string> days)
    {
        var labels = new List<string>();
        foreach (string day in WeekDays)
        {
            if (days.Contains(day))
            {
                labels.Add(Messages.DayLabel(day));
            }
        }

        if (labels.Count == WeekDays.Length)
        {
            return EveryDay;
        }

        return string.Join(", ", labels);
    }

    /// <summary>A departure time as Fleet keeps it ("22:30", IST) shown as "10:30 PM IST"; anything unreadable is shown as sent.</summary>
    public static string DepartureTime(string departureTime)
    {
        bool isTime = TimeOnly.TryParseExact(departureTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time);
        if (!isTime)
        {
            return departureTime;
        }

        return time.ToString("h:mm tt", CultureInfo.InvariantCulture) + " IST";
    }

    /// <summary>"{route name}, {departure time}" — schedules have no name of their own in Fleet.</summary>
    public static string ScheduleName(string routeName, string departureTime)
    {
        return routeName + ", " + DepartureTime(departureTime);
    }

    /// <summary>Whole rupees shown as "₹1,200".</summary>
    public static string Rupees(int fare)
    {
        return "₹" + fare.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// "Lower berth: ₹1,200, Upper berth: ₹1,000", in the bus's seat-class order when it is known
    /// (classes it does not name come last), otherwise in the order given.
    /// </summary>
    public static string FaresText(IReadOnlyList<ClassFare> fares, IReadOnlyList<string>? seatClassOrder)
    {
        var ordered = new List<ClassFare>();
        if (seatClassOrder != null)
        {
            foreach (string seatClass in seatClassOrder)
            {
                ClassFare? match = fares.FirstOrDefault(fare => string.Equals(fare.SeatClass, seatClass, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    ordered.Add(match);
                }
            }
        }

        foreach (ClassFare fare in fares)
        {
            if (!ordered.Contains(fare))
            {
                ordered.Add(fare);
            }
        }

        var parts = new List<string>();
        foreach (ClassFare fare in ordered)
        {
            parts.Add(fare.SeatClass + ": " + Rupees(fare.Fare));
        }

        return string.Join(", ", parts);
    }

    /// <summary>A fare typed in a box: whole rupees from ₹50 to ₹10,000 with digits only, or null.</summary>
    public static int? Fare(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int fare))
        {
            return null;
        }

        if (fare < MinFare || fare > MaxFare)
        {
            return null;
        }

        return fare;
    }

    /// <summary>The catalog's word for a trip status as Fleet spells it.</summary>
    public static string TripStatusLabel(string status)
    {
        switch (status)
        {
            case "scheduled":
                return Messages.StateScheduled;
            case "departed":
                return Messages.StateDeparted;
            default:
                return Messages.StateCancelled;
        }
    }

    /// <summary>An operator's bus page.</summary>
    public static string BusAddress(Guid busId)
    {
        return "/operator/buses/" + busId;
    }

    /// <summary>The bus-inactive message with the link to the bus, where it can be activated.</summary>
    public static PageAlert BusInactiveAlert(Guid busId)
    {
        return PageAlert.Danger(Messages.ProblemBusInactive).WithLink(OpenBusText, BusAddress(busId));
    }

    /// <summary>
    /// The overlap message, each conflict written as "26 Oct 2026 (a trip leaving 10:00 PM IST)" from
    /// the problem's conflicts.
    /// </summary>
    public static PageAlert OverlapAlert(ApiProblem problem)
    {
        var conflicts = new List<string>();
        bool hasConflicts = problem.Extensions.TryGetValue("conflicts", out JsonElement list) && list.ValueKind == JsonValueKind.Array;
        if (hasConflicts)
        {
            foreach (JsonElement conflict in list.EnumerateArray())
            {
                conflicts.Add(ConflictText(conflict));
            }
        }

        return PageAlert.Danger(Messages.ProblemBusTripOverlap(string.Join(", ", conflicts)));
    }

    private static string ConflictText(JsonElement conflict)
    {
        string serviceDate = Property(conflict, "serviceDate");
        string departureAt = Property(conflict, "departureAt");
        if (DateOnly.TryParseExact(serviceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            serviceDate = IndiaTime.FormatDay(date);
        }

        if (DateTimeOffset.TryParse(departureAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset moment))
        {
            departureAt = IndiaTime.FormatTime(moment);
        }

        return $"{serviceDate} (a trip leaving {departureAt})";
    }

    private static DateOnly? DateOrDefault(string? text, DateOnly whenEmpty)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return whenEmpty;
        }

        bool isDate = DateOnly.TryParseExact(text.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date);
        if (!isDate)
        {
            return null;
        }

        return date;
    }

    private static string Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
        {
            return string.Empty;
        }

        return value.GetString() ?? string.Empty;
    }
}
