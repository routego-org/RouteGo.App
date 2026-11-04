using System.Globalization;

namespace RouteGo.App.Common.UI;

/// <summary>One fare box of the schedule form: the seat class it is for and the rupees typed, kept as text.</summary>
public sealed class ScheduleFormFare   // mutable class: must round-trip on re-render
{
    public string? SeatClass { get; set; }

    public string? Fare { get; set; }
}

/// <summary>
/// The schedule form as typed, shared by the add and change pages. Values stay text where a bad
/// value must be shown again as typed.
/// </summary>
public sealed class ScheduleFormInput   // mutable class: must round-trip on re-render
{
    public Guid? RouteId { get; set; }

    public Guid? BusId { get; set; }

    /// <summary>HH:mm on the 24-hour clock, IST, as typed.</summary>
    public string? DepartureTime { get; set; }

    /// <summary>Fleet's day words of the ticked days ("monday").</summary>
    public List<string> Days { get; set; } = new List<string>();

    /// <summary>One box per seat class of the bus the boxes were drawn for, in the bus's order.</summary>
    public List<ScheduleFormFare> Fares { get; set; } = new List<ScheduleFormFare>();

    /// <summary>
    /// Redraws the fare boxes for a bus's seat classes, in its order. A fare already typed is kept
    /// for a class whose name is the same, ignoring capitals; every other box starts empty.
    /// </summary>
    public void UseSeatClasses(IReadOnlyList<string> seatClasses)
    {
        var fares = new List<ScheduleFormFare>();
        foreach (string seatClass in seatClasses)
        {
            ScheduleFormFare? typed = Fares.FirstOrDefault(fare => string.Equals(fare.SeatClass, seatClass, StringComparison.OrdinalIgnoreCase));
            string? fareText = null;
            if (typed != null)
            {
                fareText = typed.Fare;
            }

            fares.Add(new ScheduleFormFare { SeatClass = seatClass, Fare = fareText });
        }

        Fares = fares;
    }

    /// <summary>Fare boxes filled with saved fares, in the order given.</summary>
    public static List<ScheduleFormFare> FaresFrom(IReadOnlyList<ClassFare> fares)
    {
        var formFares = new List<ScheduleFormFare>();
        foreach (ClassFare fare in fares)
        {
            string fareText = fare.Fare.ToString(CultureInfo.InvariantCulture);
            formFares.Add(new ScheduleFormFare { SeatClass = fare.SeatClass, Fare = fareText });
        }

        return formFares;
    }

    /// <summary>The ticked days in week order, unknown words left out.</summary>
    public List<string> DaysInWeekOrder()
    {
        var days = new List<string>();
        foreach (string day in TripDisplay.WeekDays)
        {
            if (Days.Contains(day))
            {
                days.Add(day);
            }
        }

        return days;
    }

    /// <summary>The departure time written as Fleet takes it ("09:30" for "9:30"), or null when it is not a time of day.</summary>
    public string? DepartureTimeForFleet()
    {
        string text = DepartureTime ?? string.Empty;
        string[] formats = new[] { "H:mm", "HH:mm" };
        bool isTime = TimeOnly.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time);
        if (!isTime)
        {
            return null;
        }

        return time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }
}

/// <summary>A route or bus offered in the schedule form.</summary>
public sealed record ScheduleFormChoice(Guid Id, string Name, bool IsActive)
{
    /// <summary>The name, marked "(inactive)" for the schedule's own route or bus that is no longer active.</summary>
    public string Label()
    {
        if (IsActive)
        {
            return Name;
        }

        return Name + " (inactive)";
    }
}

/// <summary>What the shared schedule form partial draws: the form, its choices, and where its buttons go.</summary>
public sealed class ScheduleFormView
{
    public required ScheduleFormInput Input { get; init; }

    public required IReadOnlyList<ScheduleFormChoice> Routes { get; init; }

    public required IReadOnlyList<ScheduleFormChoice> Buses { get; init; }

    /// <summary>Where "Save schedule" (and "Save and change trips") posts.</summary>
    public required string SaveAddress { get; init; }

    /// <summary>Where "Show fares for this bus" posts: the page is drawn again with the chosen bus's fare boxes.</summary>
    public required string ShowFaresAddress { get; init; }

    public required string CancelAddress { get; init; }

    /// <summary>Whether a schedule is being changed: the form then says fares near departure stay as they were.</summary>
    public bool IsChange { get; init; }

    /// <summary>Where "Go back" posts while the trip-change question is open; null when no question is asked.</summary>
    public string? GoBackAddress { get; init; }
}

/// <summary>A schedule as Fleet takes it, to create or to replace (every field is sent each time).</summary>
public sealed record ScheduleRequest
{
    public required Guid RouteId { get; init; }

    public required Guid BusId { get; init; }

    public required string DepartureTime { get; init; }

    public required IReadOnlyList<string> DaysOfWeek { get; init; }

    public required IReadOnlyList<ClassFare> ClassFares { get; init; }

    /// <summary>The request for a form whose checks passed, so every field is there and readable.</summary>
    public static ScheduleRequest From(ScheduleFormInput input)
    {
        var fares = new List<ClassFare>();
        foreach (ScheduleFormFare fare in input.Fares)
        {
            fares.Add(new ClassFare(fare.SeatClass!, TripDisplay.Fare(fare.Fare)!.Value));
        }

        return new ScheduleRequest
        {
            RouteId = input.RouteId!.Value,
            BusId = input.BusId!.Value,
            DepartureTime = input.DepartureTimeForFleet()!,
            DaysOfWeek = input.DaysInWeekOrder(),
            ClassFares = fares,
        };
    }
}

/// <summary>
/// Fleet's field rules for a schedule, checked before sending so every mistake is shown at its field
/// at once. A bus that is no longer active cannot be saved with, so the page says so before Fleet does.
/// </summary>
public sealed class ScheduleFormValidator : AbstractValidator<ScheduleFormInput>
{
    public const string RouteLabel = "Route";
    public const string BusLabel = "Bus";
    public const string DaysLabel = "Days";
    public const string TimeMessage = "Enter a time from 00:00 to 23:59, like 22:30.";

    private readonly IReadOnlyCollection<Guid> _inactiveBusIds;

    public ScheduleFormValidator(IReadOnlyCollection<Guid> inactiveBusIds)
    {
        _inactiveBusIds = inactiveBusIds;
        RuleFor(x => x.RouteId).NotNull().WithMessage(Messages.ValidationRequired(RouteLabel));
        RuleFor(x => x.BusId)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage(Messages.ValidationRequired(BusLabel))
            .Must(IsActiveBus).WithMessage(Messages.ProblemBusInactive);
        RuleFor(x => x).Custom(CheckTime);
        RuleFor(x => x).Custom(CheckDays);
        RuleFor(x => x.Fares).Custom(CheckFares);
    }

    /// <summary>The key of a fare box in the page's errors ("Fares[1].Fare").</summary>
    public static string FareField(int index)
    {
        return $"{nameof(ScheduleFormInput.Fares)}[{index}].{nameof(ScheduleFormFare.Fare)}";
    }

    private bool IsActiveBus(Guid? busId)
    {
        return !_inactiveBusIds.Contains(busId!.Value);
    }

    private static void CheckTime(ScheduleFormInput input, ValidationContext<ScheduleFormInput> context)
    {
        if (input.DepartureTimeForFleet() is null)
        {
            context.AddFailure(nameof(ScheduleFormInput.DepartureTime), TimeMessage);
        }
    }

    private static void CheckDays(ScheduleFormInput input, ValidationContext<ScheduleFormInput> context)
    {
        if (input.DaysInWeekOrder().Count == 0)
        {
            context.AddFailure(nameof(ScheduleFormInput.Days), Messages.ValidationRequired(DaysLabel));
        }
    }

    private static void CheckFares(List<ScheduleFormFare> fares, ValidationContext<ScheduleFormInput> context)
    {
        for (int index = 0; index < fares.Count; index++)
        {
            if (TripDisplay.Fare(fares[index].Fare) is null)
            {
                context.AddFailure(FareField(index), Messages.ValidationFare);
            }
        }
    }
}
