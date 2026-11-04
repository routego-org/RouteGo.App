using System.Globalization;
using System.Text.RegularExpressions;

namespace RouteGo.App.Features.Bookings.BookSeats;

/// <summary>
/// What "Pay" came to: a redirect, or what to show — a message, field errors, the fares the
/// customer must see again, and the seats that can no longer stay chosen.
/// </summary>
public sealed record PayOutcome
{
    public string? RedirectTo { get; init; }

    public PageAlert? Alert { get; init; }

    /// <summary>Field errors by field key (see <see cref="BookSeatsChecks"/>); the page-level one has the empty key.</summary>
    public IReadOnlyDictionary<string, string> FieldErrors { get; init; } = new Dictionary<string, string>();

    /// <summary>The current fare of seats whose price changed, by seat number.</summary>
    public IReadOnlyDictionary<string, int> NewFares { get; init; } = new Dictionary<string, int>();

    /// <summary>Seats that are no longer there to choose, taken by someone else or not on the trip.</summary>
    public IReadOnlyList<string> Unpicked { get; init; } = Array.Empty<string>();
}

/// <summary>The words for each answer Booking gives to a booking request that is not a success.</summary>
public static class BookSeatsRefusals
{
    private const string PriceChanged = "PRICE_CHANGED";
    private const string UnpaidBookingExists = "UNPAID_BOOKING_EXISTS";

    // Booking names a refused field like "seats[0].passengerAge".
    private static readonly Regex _seatField = new Regex(@"^seats\[(\d+)\]\.(passengerName|passengerAge|passengerGender)$", RegexOptions.CultureInvariant);

    /// <summary>What Booking's refusal means for the page.</summary>
    public static PayOutcome ToOutcome(ApiResult<CreatedBooking> api, PayCommand command)
    {
        if (api.Errors != null)
        {
            return FieldOutcome(api, command.Seats);
        }

        ApiProblem? problem = api.Problem;
        if (problem is null || BookingProblems.IsUnreachable(problem))
        {
            return new PayOutcome { Alert = PageAlert.Danger(Messages.ProblemUpstream) };
        }

        return ProblemOutcome(problem, command);
    }

    private static PayOutcome ProblemOutcome(ApiProblem problem, PayCommand command)
    {
        switch (problem.Code)
        {
            case PriceChanged:
                return PriceOutcome(problem);
            case BookingProblems.SeatsUnavailable:
                return TakenOutcome(problem);
            case UnpaidBookingExists:
                return new PayOutcome { Alert = UnpaidAlert(problem) };
            case BookingProblems.BookingClosed:
                return new PayOutcome { Alert = BookingProblems.ClosedAlert(problem, command.CorrelationId) };
            case BookingProblems.TripCancelled:
                return new PayOutcome { Alert = PageAlert.Danger(Messages.ProblemTripCancelled) };
            case BookingProblems.NotFound:
                return new PayOutcome { Alert = NotBookableYetAlert(command.SameAddress) };
            case BookingProblems.UnknownSeats:
                return UnknownOutcome(problem);
            case SharedProblems.UpstreamUnavailable:
            case SharedProblems.UpstreamTimeout:
                return new PayOutcome { Alert = PageAlert.Danger("We could not complete your booking. No seats were kept for you. Please try again.") };
            default:
                return new PayOutcome { Alert = SharedProblems.ToAlert(problem, command.CorrelationId) };
        }
    }

    private static PayOutcome PriceOutcome(ApiProblem problem)
    {
        var fares = new Dictionary<string, int>();
        if (problem.Extensions.TryGetValue("seatFares", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in list.EnumerateArray())
            {
                string seatNo = item.GetProperty("seatNo").GetString() ?? string.Empty;
                fares[seatNo] = item.GetProperty("fare").GetInt32();
            }
        }

        string total = TripDisplay.Rupees(int.Parse(problem.GetExtension("currentTotal") ?? "0", CultureInfo.InvariantCulture));
        string text = $"The price changed. The new total is {total}. Please check it and choose Pay again.";
        return new PayOutcome { Alert = PageAlert.Warning(text), NewFares = fares };
    }

    private static PayOutcome TakenOutcome(ApiProblem problem)
    {
        List<string> seatNos = SeatDisplay.ExtensionSeats(problem, "unavailableSeats");
        string verb = "was";
        if (seatNos.Count > 1)
        {
            verb = "were";
        }

        string text = $"{SeatDisplay.SeatList(seatNos)} {verb} just taken by someone else. Please choose other seats.";
        return new PayOutcome { Alert = PageAlert.Danger(text), Unpicked = seatNos };
    }

    private static PayOutcome UnknownOutcome(ApiProblem problem)
    {
        List<string> seatNos = SeatDisplay.ExtensionSeats(problem, "unknownSeats");
        string text = "Some seats are no longer on this trip. We loaded the seats again. Please choose again.";
        return new PayOutcome { Alert = PageAlert.Danger(text), Unpicked = seatNos };
    }

    private static PageAlert UnpaidAlert(ApiProblem problem)
    {
        string until = IndiaTime.FormatIso(problem.GetExtension("holdExpiresAt")) ?? string.Empty;
        string text = $"You already have a booking waiting for payment. Its seats are held until {until}.";
        string address = BookingDisplay.MyBookingsAddress;
        if (Guid.TryParse(problem.GetExtension("bookingId"), out Guid bookingId))
        {
            address = BookingDisplay.BookingAddress(bookingId);
        }

        return PageAlert.Danger(text).WithLink("Continue with that booking", address);
    }

    private static PageAlert NotBookableYetAlert(string sameAddress)
    {
        var alert = PageAlert.Danger("This trip can't be booked yet. If it was put on sale just now, try again in a minute.");
        return alert.WithLink(Messages.LinkTryAgain, sameAddress);
    }

    // Booking's field errors come under the box they name; a field the page has no box for is shown at the top.
    private static PayOutcome FieldOutcome(ApiResult<CreatedBooking> api, IReadOnlyList<string> seats)
    {
        var errors = new Dictionary<string, string>();
        foreach (KeyValuePair<string, string[]> error in api.Errors!)
        {
            errors[KeyOf(error.Key, seats)] = error.Value.FirstOrDefault() ?? string.Empty;
        }

        return new PayOutcome { FieldErrors = errors };
    }

    private static string KeyOf(string bookingField, IReadOnlyList<string> seats)
    {
        if (bookingField == "contactName")
        {
            return BookSeatsChecks.ContactKey;
        }

        Match match = _seatField.Match(bookingField);
        int index = -1;
        if (match.Success)
        {
            index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        if (index < 0 || index >= seats.Count)
        {
            return BookSeatsChecks.PageKey;
        }

        return BookSeatsChecks.Key(seats[index], FieldName(match.Groups[2].Value));
    }

    private static string FieldName(string bookingName)
    {
        switch (bookingName)
        {
            case "passengerName":
                return "name";
            case "passengerAge":
                return "age";
            default:
                return "gender";
        }
    }
}
