using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace RouteGo.App.Features.Operator.Trips;

/// <summary>
/// What the trip list decides with the portal's clock: which actions each trip offers. Fleet
/// decides again when an action is sent.
/// </summary>
public static class TripActions
{
    /// <summary>Fleet freezes a trip's fares this long before it leaves.</summary>
    public static readonly TimeSpan FareCutoff = TimeSpan.FromHours(6);

    /// <summary>A scheduled trip leaving more than 6 hours from now.</summary>
    public static bool CanChangeFares(TripDetails trip, DateTimeOffset now)
    {
        return trip.IsScheduled && trip.DepartureAt - now > FareCutoff;
    }

    /// <summary>A scheduled trip leaving within 6 hours: its fares are frozen.</summary>
    public static bool IsFrozen(TripDetails trip, DateTimeOffset now)
    {
        return trip.IsScheduled && !CanChangeFares(trip, now);
    }

    /// <summary>Whether every typed fare equals the trip's fare for the same seat class.</summary>
    public static bool IsUnchanged(TripFaresInput input, TripDetails trip)
    {
        foreach (ScheduleFormFare typed in input.Fares)
        {
            ClassFare? current = trip.ClassFares.FirstOrDefault(fare => fare.SeatClass == typed.SeatClass);
            if (current is null || TripDisplay.Fare(typed.Fare) != current.Fare)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>"{route} on {travel date} at {departs}", for example "Mumbai → Pune on 9 Oct 2026 at 10:30 PM IST".</summary>
    public static string When(TripDetails trip)
    {
        return $"{trip.RouteName()} on {IndiaTime.FormatDay(trip.ServiceDate)} at {IndiaTime.FormatTime(trip.DepartureAt)}";
    }

}

/// <summary>
/// The spec's words for Fleet's refusals of a fare change or cancellation that mean the trip moved
/// on while the page was open; after each, the list is loaded again.
/// </summary>
public static class TripRefusals
{
    private const string CutoffPassed = "FARE_CHANGE_CUTOFF_PASSED";
    private const string TripDeparted = "TRIP_DEPARTED";
    private const string TripNotScheduled = "TRIP_NOT_SCHEDULED";
    private const string Entity = "trip";
    private const string ReasonField = "reason";
    private const string FareFieldStart = "classFares";
    private const string FromField = "from";
    private const string ToField = "to";

    /// <summary>The alert for a refusal about the trip's state; null for any other refusal.</summary>
    public static PageAlert? TripStateAlert(ApiProblem problem)
    {
        switch (problem.Code)
        {
            case CutoffPassed:
                string frozeAt = IndiaTime.FormatIso(problem.GetExtension("cutoffAt")) ?? string.Empty;
                return PageAlert.Danger($"Fares are frozen 6 hours before departure. This trip's fares froze at {frozeAt}.");
            case TripDeparted:
                return PageAlert.Danger(Messages.ProblemTripDeparted);
            case TripNotScheduled:
                string currentStatus = TripDisplay.TripStatusLabel(problem.GetExtension("currentStatus") ?? string.Empty);
                return PageAlert.Danger(Messages.ProblemTripNotScheduled(currentStatus.ToLowerInvariant()));
            case TripDisplay.ClassFaresMismatch:
                return PageAlert.Danger(Messages.ProblemClassFaresMismatch);
            case FleetDisplay.NotFound:
                return PageAlert.Danger(Messages.ProblemNotFound(Entity));
            default:
                return null;
        }
    }

    /// <summary>
    /// Puts Fleet's field errors at the open form's field in the page's own words: every fare box
    /// when it names the fares, the reason when it names the reason. False when it names neither,
    /// so the page tells Fleet's title at the top.
    /// </summary>
    public static bool PlaceFieldErrors(IReadOnlyDictionary<string, string[]> errors, bool isFareChange, int fareCount, ModelStateDictionary modelState)
    {
        if (isFareChange && NamesField(errors, FareFieldStart))
        {
            for (int index = 0; index < fareCount; index++)
            {
                modelState.AddModelError("FaresInput." + TripFaresValidator.FareField(index), Messages.ValidationFare);
            }

            return true;
        }

        if (!isFareChange && NamesField(errors, ReasonField))
        {
            modelState.AddModelError("CancelInput." + nameof(TripCancelInput.Reason), Messages.ValidationReasonLength);
            return true;
        }

        return false;
    }

    /// <summary>Whether Fleet's field errors are about the travel dates: the page then shows the range message.</summary>
    public static bool NamesDates(IReadOnlyDictionary<string, string[]> errors)
    {
        return NamesField(errors, FromField) || NamesField(errors, ToField);
    }

    private static bool NamesField(IReadOnlyDictionary<string, string[]> errors, string fieldStart)
    {
        foreach (string field in errors.Keys)
        {
            if (field.StartsWith(fieldStart, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
