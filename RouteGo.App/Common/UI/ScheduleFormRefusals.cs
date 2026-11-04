using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace RouteGo.App.Common.UI;

/// <summary>
/// Where Fleet's refusals of a schedule land on the schedule form, for both the add and change
/// pages: at the field they are about when the form shows it, otherwise at the top. Everything typed
/// is kept, except a route or bus Fleet no longer knows.
/// </summary>
public static class ScheduleFormRefusals
{
    public const string RouteInactiveMessage = "This route is inactive. Choose another route.";
    public const string UnknownBusMessage = "We could not find this bus among your buses. We have loaded your buses again.";
    public const string UnknownRouteMessage = "We could not find this route among your routes. We have loaded your routes again.";
    public const string SeatClassesFailedMessage = "We could not load this bus's seat classes. Choose the bus again.";

    private const string InputPrefix = "Input.";
    private const string RouteInactive = "ROUTE_INACTIVE";
    private const string UnknownBus = "UNKNOWN_BUS";
    private const string UnknownRoute = "UNKNOWN_ROUTE";
    private const string FareFieldStart = "classFares[";
    private const string Entity = "schedule";

    /// <summary>
    /// Puts Fleet's field errors at the form's fields in the page's own words. Returns the alert for
    /// an error on a field the form does not show (Fleet's title), or null.
    /// </summary>
    public static PageAlert? PlaceFieldErrors(IReadOnlyDictionary<string, string[]> errors, string? title, ModelStateDictionary modelState)
    {
        PageAlert? alert = null;
        foreach (KeyValuePair<string, string[]> error in errors)
        {
            string? field = FormField(error.Key);
            if (field is null)
            {
                alert = PageAlert.Danger(title ?? error.Value[0]);
                continue;
            }

            modelState.AddModelError(InputPrefix + field, FieldMessage(field));
        }

        return alert;
    }

    /// <summary>
    /// Shows a refusal: at the route or bus field when it is about that choice, otherwise as the
    /// returned alert. A route or bus Fleet does not know is cleared, since the lists were loaded again.
    /// </summary>
    public static PageAlert? Show(ApiProblem problem, ScheduleFormInput input, ModelStateDictionary modelState, string correlationId)
    {
        switch (problem.Code)
        {
            case TripDisplay.BusInactive:
                modelState.AddModelError(InputPrefix + nameof(ScheduleFormInput.BusId), Messages.ProblemBusInactive);
                return null;
            case RouteInactive:
                modelState.AddModelError(InputPrefix + nameof(ScheduleFormInput.RouteId), RouteInactiveMessage);
                return null;
            case UnknownBus:
                input.BusId = null;
                return PageAlert.Danger(UnknownBusMessage);
            case UnknownRoute:
                input.RouteId = null;
                return PageAlert.Danger(UnknownRouteMessage);
            case TripDisplay.ClassFaresMismatch:
                return PageAlert.Danger(Messages.ProblemClassFaresMismatch);
            case TripDisplay.BusTripOverlap:
                return TripDisplay.OverlapAlert(problem);
            default:
                return FleetDisplay.ToAlert(problem, Entity, correlationId);
        }
    }

    // Fleet's field name -> the form's field ("classFares[1].fare" -> "Fares[1].Fare"); null when the form does not show it.
    private static string? FormField(string fleetField)
    {
        if (string.Equals(fleetField, "routeId", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(ScheduleFormInput.RouteId);
        }

        if (string.Equals(fleetField, "busId", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(ScheduleFormInput.BusId);
        }

        if (string.Equals(fleetField, "departureTime", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(ScheduleFormInput.DepartureTime);
        }

        if (fleetField.StartsWith("daysOfWeek", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(ScheduleFormInput.Days);
        }

        int? fareIndex = FareIndex(fleetField);
        if (fareIndex is null)
        {
            return null;
        }

        return ScheduleFormValidator.FareField(fareIndex.Value);
    }

    private static string FieldMessage(string field)
    {
        if (field == nameof(ScheduleFormInput.RouteId))
        {
            return Messages.ValidationRequired(ScheduleFormValidator.RouteLabel);
        }

        if (field == nameof(ScheduleFormInput.BusId))
        {
            return Messages.ValidationRequired(ScheduleFormValidator.BusLabel);
        }

        if (field == nameof(ScheduleFormInput.DepartureTime))
        {
            return ScheduleFormValidator.TimeMessage;
        }

        if (field == nameof(ScheduleFormInput.Days))
        {
            return Messages.ValidationRequired(ScheduleFormValidator.DaysLabel);
        }

        return Messages.ValidationFare;
    }

    // "classFares[2].fare" -> 2; null for any other field.
    private static int? FareIndex(string fleetField)
    {
        if (!fleetField.StartsWith(FareFieldStart, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int end = fleetField.IndexOf(']', StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        string indexText = fleetField.Substring(FareFieldStart.Length, end - FareFieldStart.Length);
        bool isIndex = int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out int index);
        if (!isIndex)
        {
            return null;
        }

        return index;
    }
}
