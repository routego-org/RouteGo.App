using System.Globalization;

namespace RouteGo.App.Features.Operator.NewRoute;

/// <summary>
/// One stop as typed. Hours and minutes stay text so a value such as "1.5" is kept on the page and
/// told apart from an empty box; the first stop's are never read, it is always at 0.
/// </summary>
public sealed class NewRouteStopInput   // mutable class: must round-trip on re-render
{
    public const int MaxHours = 48;
    public const int MaxMinutes = 59;

    private const int MinutesPerHour = 60;

    public Guid? CityId { get; set; }

    public string? Hours { get; set; }

    public string? Minutes { get; set; }

    /// <summary>The time from start in whole minutes, or null when hours or minutes are missing or not allowed.</summary>
    public int? TotalMinutes()
    {
        int? hours = NewRouteInput.WholeNumber(Hours);
        int? minutes = NewRouteInput.WholeNumber(Minutes);
        if (hours is null || minutes is null)
        {
            return null;
        }

        if (hours > MaxHours || minutes > MaxMinutes)
        {
            return null;
        }

        return (hours.Value * MinutesPerHour) + minutes.Value;
    }
}

public sealed class NewRouteInput   // mutable class: must round-trip on re-render
{
    public const int MinStops = 2;
    public const int MaxStops = 12;

    /// <summary>The stops in order: the first stop, any stops on the way, the last stop.</summary>
    public List<NewRouteStopInput> Stops { get; set; } = new List<NewRouteStopInput>();

    public string? DistanceKm { get; set; }

    /// <summary>
    /// A whole number typed with digits only ("150", " 150 "), or null for anything else — empty,
    /// "150.5", "-3", "abc", or a number too big to hold.
    /// </summary>
    public static int? WholeNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            return null;
        }

        return value;
    }

    /// <summary>Makes sure the first and the last stop are there: a new form, or a post that lost them.</summary>
    public void EnsureFirstAndLast()
    {
        while (Stops.Count < MinStops)
        {
            Stops.Add(new NewRouteStopInput());
        }
    }

    public bool CanAddStop()
    {
        return Stops.Count < MaxStops;
    }

    /// <summary>Adds an empty stop just before the last stop, unless the route already has the most stops.</summary>
    public void AddStop()
    {
        if (!CanAddStop())
        {
            return;
        }

        Stops.Insert(Stops.Count - 1, new NewRouteStopInput());
    }

    /// <summary>Whether the stop at the index is one on the way; the first and the last stop cannot be removed.</summary>
    public bool CanRemoveStop(int index)
    {
        return index > 0 && index < Stops.Count - 1;
    }

    public void RemoveStop(int index)
    {
        if (!CanRemoveStop(index))
        {
            return;
        }

        Stops.RemoveAt(index);
    }

    /// <summary>Empties every city choice; hours, minutes and distance stay as typed.</summary>
    public void ClearCities()
    {
        foreach (NewRouteStopInput stop in Stops)
        {
            stop.CityId = null;
        }
    }
}

/// <summary>
/// Fleet's rules for a new route, checked before sending so every mistake is shown at its stop at
/// once. Needs the city list to name a city chosen twice.
/// </summary>
public sealed class NewRouteValidator : AbstractValidator<NewRouteInput>
{
    public const int MaxJourneyMinutes = 2880;
    public const int MinDistanceKm = 1;
    public const int MaxDistanceKm = 3000;
    public const string CityLabel = "City";
    public const string DistanceLabel = "Distance (km)";
    public const string TimeInvalidMessage = "Enter hours (0 to 48) and minutes (0 to 59).";
    public const string TimeNotLaterMessage = "This stop must come later than the stop before it.";
    public const string TimeTooLongMessage = "A journey can take at most 48 hours.";

    private readonly IReadOnlyList<City> _cities;

    public NewRouteValidator(IReadOnlyList<City> cities)
    {
        _cities = cities;
        RuleFor(x => x.Stops).Custom(CheckStops);
        RuleFor(x => x.DistanceKm)
            .Must(IsDistance).WithMessage(Messages.ValidationRange(DistanceLabel, MinDistanceKm, MaxDistanceKm));
    }

    /// <summary>The key of a stop's field in the page's errors ("Stops[2].CityId").</summary>
    public static string StopField(int index, string field)
    {
        return $"{nameof(NewRouteInput.Stops)}[{index}].{field}";
    }

    private static bool IsDistance(string? distanceKm)
    {
        int? distance = NewRouteInput.WholeNumber(distanceKm);
        if (distance is null)
        {
            return false;
        }

        return distance.Value >= MinDistanceKm && distance.Value <= MaxDistanceKm;
    }

    private void CheckStops(List<NewRouteStopInput> stops, ValidationContext<NewRouteInput> context)
    {
        var chosen = new HashSet<Guid>();
        int previousMinutes = 0;
        for (int index = 0; index < stops.Count; index++)
        {
            CheckCity(stops[index], index, chosen, context);
            if (index == 0)
            {
                continue;
            }

            int? minutes = stops[index].TotalMinutes();
            string? timeProblem = TimeProblem(minutes, previousMinutes, index == stops.Count - 1);
            if (timeProblem != null)
            {
                context.AddFailure(StopField(index, nameof(NewRouteStopInput.Hours)), timeProblem);
            }

            // A stop whose time cannot be read is skipped: the next stop is compared with the last time known.
            if (minutes != null)
            {
                previousMinutes = minutes.Value;
            }
        }
    }

    private void CheckCity(NewRouteStopInput stop, int index, HashSet<Guid> chosen, ValidationContext<NewRouteInput> context)
    {
        string field = StopField(index, nameof(NewRouteStopInput.CityId));
        if (stop.CityId is null)
        {
            context.AddFailure(field, Messages.ValidationRequired(CityLabel));
            return;
        }

        // The first stop to choose a city keeps it; every later one is told.
        if (!chosen.Add(stop.CityId.Value))
        {
            context.AddFailure(field, $"{CityName(stop.CityId.Value)} is already a stop on this route.");
        }
    }

    private static string? TimeProblem(int? minutes, int previousMinutes, bool isLastStop)
    {
        if (minutes is null)
        {
            return TimeInvalidMessage;
        }

        if (minutes <= previousMinutes)
        {
            return TimeNotLaterMessage;
        }

        if (isLastStop && minutes > MaxJourneyMinutes)
        {
            return TimeTooLongMessage;
        }

        return null;
    }

    private string CityName(Guid cityId)
    {
        foreach (City city in _cities)
        {
            if (city.CityId == cityId)
            {
                return city.Name;
            }
        }

        return CityLabel;
    }
}
