using System.Globalization;

namespace RouteGo.App.Common.UI;

/// <summary>
/// The recurring user-facing messages of the portal, copied word for word from the product specs'
/// message catalog (design/message-catalog.md). Each constant is named after its catalog key
/// (problem.upstream -> ProblemUpstream), so a page's words can always be traced to one source.
/// Feature-specific words stay in the feature's own slice.
/// </summary>
public static class Messages
{
    public const string SessionEnded = "Your session has ended. Please sign in again.";
    public const string SessionSignInRequired = "Please sign in to continue.";
    public const string AccessNoPermission = "You don't have permission to open this page.";
    public const string AccessGoHome = "Go to Home";

    public const string ProblemForbidden = "You don't have access to this.";
    public const string ProblemUpstream = "This part of RouteGo is not available right now. Please try again in a few minutes.";
    public const string ProblemFormExpired = "This form has expired. Reload the page and try again.";
    public const string ProblemInvalidCredentials = "The email or password is incorrect.";
    public const string ProblemEmailAlreadyRegistered = "This email is already registered. Sign in instead.";
    public const string ProblemGstinAlreadyRegistered = "This GSTIN is already registered with RouteGo. If you think this is wrong, contact RouteGo support.";
    public const string ProblemAccountSuspended = "Your account is suspended. Please contact RouteGo support.";
    public const string ProblemOperatorProfileNotReady = "Your operator account is being set up. Please try again in a minute.";

    public const string ProblemBusInactive = "This bus is inactive. Activate the bus first, or choose another bus.";
    public const string ProblemClassFaresMismatch = "The fares do not match this bus's seat classes. We have loaded the seat classes again. Enter one fare for each.";
    public const string ProblemScheduleInactive = "This schedule is stopped. Restart it first.";
    public const string ProblemTripDeparted = "This trip has already left. Nothing was changed.";

    public const string ConfirmTripsWillBeCancelled = "Upcoming trips of this schedule will be cancelled, and customers who booked them will get a refund. This cannot be undone. Do you want to continue?";

    public const string StateActive = "Active";
    public const string StateInactive = "Inactive";
    public const string StateScheduled = "Scheduled";
    public const string StateDeparted = "Departed";
    public const string StateCancelled = "Cancelled";
    public const string StateAvailable = "Available";
    public const string StateHeld = "On hold";
    public const string StateBooked = "Booked";
    public const string StateBlocked = "Blocked";
    public const string StateSoldOut = "Sold out";

    public const string BlockReasonOperator = "Operator";
    public const string BlockReasonTripCancelled = "Trip cancelled";
    public const string NoticeTripCancelled = "This trip was cancelled. Its seats will not be sold.";
    public const string NoticeTripDeparted = "This trip has left.";
    public const string LinkTryAgain = "Try again";
    public const string LinkPublicSeatMap = "See the public seat map";
    public const string ProblemTripCancelled = "This trip was cancelled. Nothing was changed.";
    public const string ProblemSeatDetailsUnavailable = "Seat details for this trip are not available. If the trip was put on sale just now, try again in a minute. Seat details are not kept for trips that left more than a few days ago.";

    public const string SuccessSignedOut = "You have signed out.";

    public const string RoleCustomer = "Customer";
    public const string RoleOperator = "Operator";
    public const string RoleAdmin = "Admin";

    /// <summary>
    /// role.customer / role.operator / role.admin for a role as Identity spells it. Case is
    /// ignored because the access token says "Admin" while the profile answer says "admin".
    /// </summary>
    public static string RoleLabel(string role)
    {
        if (string.Equals(role, PortalSession.RoleAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return RoleAdmin;
        }

        if (string.Equals(role, PortalSession.RoleOperator, StringComparison.OrdinalIgnoreCase))
        {
            return RoleOperator;
        }

        return RoleCustomer;
    }

    /// <summary>success.created.</summary>
    public static string SuccessCreated(string entity)
    {
        return $"{entity} created.";
    }

    /// <summary>success.updated.</summary>
    public static string SuccessUpdated(string entity)
    {
        return $"{entity} updated.";
    }

    /// <summary>success.deactivated.</summary>
    public static string SuccessDeactivated(string entity)
    {
        return $"{entity} deactivated.";
    }

    /// <summary>success.activated.</summary>
    public static string SuccessActivated(string entity)
    {
        return $"{entity} activated.";
    }

    /// <summary>success.deleted.</summary>
    public static string SuccessDeleted(string entity)
    {
        return $"{entity} deleted.";
    }

    /// <summary>problem.bus_in_use, with the next departure already formatted in IST.</summary>
    public static string ProblemBusInUse(string tripCount, string nextDeparture)
    {
        return $"This bus has {tripCount} upcoming trips. The next one leaves on {nextDeparture}. Stop its schedules or move them to another bus first.";
    }

    public const string FlagYes = "Yes";
    public const string FlagNo = "No";

    /// <summary>flag.yes_no.</summary>
    public static string YesNo(bool value)
    {
        if (value)
        {
            return FlagYes;
        }

        return FlagNo;
    }

    /// <summary>bus_type.* for a bus type as Fleet spells it.</summary>
    public static string BusTypeLabel(string busType)
    {
        switch (busType)
        {
            case "sleeper":
                return "Sleeper";
            case "semiSleeper":
                return "Semi-sleeper";
            default:
                return "Seater";
        }
    }

    /// <summary>amenity.* for an amenity as Fleet spells it.</summary>
    public static string AmenityLabel(string amenity)
    {
        switch (amenity)
        {
            case "wifi":
                return "Wi-Fi";
            case "chargingPoint":
                return "Charging point";
            case "blanket":
                return "Blanket";
            case "waterBottle":
                return "Water bottle";
            case "readingLight":
                return "Reading light";
            case "cctv":
                return "CCTV";
            case "gpsTracking":
                return "GPS tracking";
            default:
                return "Restroom";
        }
    }

    /// <summary>deck.lower / deck.upper for a deck as Fleet spells it.</summary>
    public static string DeckLabel(string deck)
    {
        if (deck == "upper")
        {
            return "Upper deck";
        }

        return "Lower deck";
    }

    /// <summary>empty.collection, naming the items in lower case ("approved companies").</summary>
    public static string EmptyCollection(string items)
    {
        return $"No {items} yet.";
    }

    /// <summary>empty.search, naming the items in lower case ("companies").</summary>
    public static string EmptySearch(string items)
    {
        return $"No {items} match your search.";
    }

    /// <summary>empty.filtered, naming the items in lower case ("routes").</summary>
    public static string EmptyFiltered(string items)
    {
        return $"No {items} match the current filters.";
    }

    /// <summary>problem.not_found, naming the thing in lower case mid-sentence ("route").</summary>
    public static string ProblemNotFound(string entity)
    {
        return $"We couldn't find that {entity}. It may have been removed.";
    }

    /// <summary>problem.page_not_found.</summary>
    public const string ProblemPageNotFound = "We couldn't find that page. Check the address, or go to Home.";

    /// <summary>problem.internal_error, with the reference support needs.</summary>
    public static string ProblemInternalError(string traceId)
    {
        return $"Something went wrong on our side. Try again — if it keeps happening, contact support with reference {traceId}.";
    }

    /// <summary>problem.account_locked, with the time the lock ends (already formatted in IST).</summary>
    public static string ProblemAccountLocked(string lockedUntil)
    {
        return $"Your account is locked for a short time because of too many wrong passwords. Try again after {lockedUntil}.";
    }

    /// <summary>validation.required.</summary>
    public static string ValidationRequired(string field)
    {
        return $"{field} is required.";
    }

    /// <summary>validation.format.</summary>
    public static string ValidationFormat(string field)
    {
        return $"Enter a valid {field}.";
    }

    /// <summary>validation.range, with the limits written with thousands separators ("3,000").</summary>
    public static string ValidationRange(string field, int min, int max)
    {
        string minText = min.ToString("N0", CultureInfo.InvariantCulture);
        string maxText = max.ToString("N0", CultureInfo.InvariantCulture);
        return $"{field} must be between {minText} and {maxText}.";
    }

    /// <summary>validation.max_length.</summary>
    public static string ValidationMaxLength(string field, int length)
    {
        return $"{field} must be {length} characters or fewer.";
    }

    /// <summary>validation.min_length.</summary>
    public static string ValidationMinLength(string field, int length)
    {
        return $"{field} must be at least {length} characters.";
    }

    /// <summary>problem.bus_trip_overlap, with the conflicting trips already written out.</summary>
    public static string ProblemBusTripOverlap(string conflicts)
    {
        return $"This bus would be on two trips at the same time. A bus needs 60 minutes between arriving and leaving again. It is already busy on: {conflicts}. Choose another bus or another time.";
    }

    /// <summary>problem.trip_not_scheduled, with the trip's status word in lower case ("cancelled").</summary>
    public static string ProblemTripNotScheduled(string currentStatus)
    {
        return $"This trip is {currentStatus}. Nothing was changed.";
    }

    /// <summary>day.* for a day of the week as Fleet spells it ("monday").</summary>
    public static string DayLabel(string day)
    {
        switch (day)
        {
            case "monday":
                return "Monday";
            case "tuesday":
                return "Tuesday";
            case "wednesday":
                return "Wednesday";
            case "thursday":
                return "Thursday";
            case "friday":
                return "Friday";
            case "saturday":
                return "Saturday";
            default:
                return "Sunday";
        }
    }

    public const string ValidationReasonLength = "Reason must be between 10 and 500 characters.";
    public const string ValidationFare = "Fare must be a whole number of rupees from ₹50 to ₹10,000.";
    public const string ValidationDateRange = "Choose a To date on or after the From date, at most 31 days apart.";
    public const string ValidationPasswordPolicy = "Password must be at least 10 characters and have at least one letter and one number.";
    public const string ValidationPhone = "Enter a 10-digit Indian mobile number that starts with 6, 7, 8 or 9.";
    public const string ValidationGstin = "Enter a valid 15-character GSTIN, for example 27AAPFU0939F1ZV.";
}
