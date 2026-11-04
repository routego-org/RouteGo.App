using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Common.UI;

/// <summary>
/// The rules every seat page shares — the public seat map and the operator's and the admin's seats
/// of a trip — written once: the page heading, the words for a seat's status and block reason, the
/// counts line, how the seats are laid out deck by deck (Features/Shared/_SeatGrid.cshtml draws
/// them), and what a page says when Seat Inventory has no seats for the trip.
/// </summary>
public static class SeatDisplay
{
    public const string Available = "available";
    public const string Held = "held";
    public const string Booked = "booked";
    public const string Blocked = "blocked";
    public const string BlockedByOperator = "operator";
    public const string NotFound = "NOT_FOUND";

    private const string LowerDeck = "lower";
    private const string UpperDeck = "upper";
    private const string RouteKey = "route";

    /// <summary>
    /// "Seats: Mumbai → Pune, 23 Oct 2026, 10:30 PM IST", or "Seats: trip leaving 23 Oct 2026,
    /// 10:30 PM IST" when the address names no route. The route is only a label the link passed.
    /// </summary>
    public static string Heading(string? route, DateTimeOffset departureAt)
    {
        string departure = IndiaTime.Format(departureAt);
        if (string.IsNullOrWhiteSpace(route))
        {
            return "Seats: trip leaving " + departure;
        }

        return $"Seats: {route.Trim()}, {departure}";
    }

    /// <summary>The catalog's word for a seat status as Seat Inventory spells it.</summary>
    public static string StatusLabel(string status)
    {
        switch (status)
        {
            case Available:
                return Messages.StateAvailable;
            case Held:
                return Messages.StateHeld;
            case Booked:
                return Messages.StateBooked;
            default:
                return Messages.StateBlocked;
        }
    }

    /// <summary>The catalog's word for why a seat is blocked, shown under "Blocked by".</summary>
    public static string BlockReasonLabel(string blockReason)
    {
        if (blockReason == BlockedByOperator)
        {
            return Messages.BlockReasonOperator;
        }

        return Messages.BlockReasonTripCancelled;
    }

    /// <summary>"Blocked by: Operator" or "Blocked by: Trip cancelled", shown on a blocked seat of the staff seat pages.</summary>
    public static SeatLine BlockedByLine(string blockReason)
    {
        return new SeatLine("Blocked by", BlockReasonLabel(blockReason));
    }

    /// <summary>The notice above the seats of a cancelled or departed trip; null for any other trip.</summary>
    public static string? Notice(bool isCancelled, bool isDeparted)
    {
        if (isCancelled)
        {
            return Messages.NoticeTripCancelled;
        }

        if (isDeparted)
        {
            return Messages.NoticeTripDeparted;
        }

        return null;
    }

    /// <summary>"Available: 2 · On hold: 1 · Booked: 1 · Blocked: 1", from every seat's status.</summary>
    public static string CountsText(IReadOnlyCollection<string> statuses)
    {
        int available = statuses.Count(status => status == Available);
        int held = statuses.Count(status => status == Held);
        int booked = statuses.Count(status => status == Booked);
        int blocked = statuses.Count(status => status == Blocked);
        return $"Available: {available} · On hold: {held} · Booked: {booked} · Blocked: {blocked}";
    }

    /// <summary>Seat numbers as a page names them: "L1, L2".</summary>
    public static string SeatList(IEnumerable<string> seatNos)
    {
        return string.Join(", ", seatNos);
    }

    /// <summary>The address with the route label passed on, when there is one.</summary>
    public static string WithRoute(string address, string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return address;
        }

        return QueryHelpers.AddQueryString(address, RouteKey, route.Trim());
    }

    /// <summary>The trip's public seat map, passing the route label on.</summary>
    public static string PublicSeatMapAddress(Guid tripId, string? route)
    {
        return WithRoute($"/trips/{tripId}/seats", route);
    }

    /// <summary>
    /// Whether loading the seats answered that Seat Inventory has none for this trip: the trip is
    /// not known (NOT_FOUND), or the address names no trip (VALIDATION_FAILED, which arrives as field errors).
    /// </summary>
    public static bool IsUnavailable<T>(ApiResult<T> api)
    {
        if (api.Errors != null)
        {
            return true;
        }

        return api.Problem != null && api.Problem.Code == NotFound;
    }

    /// <summary>"Seat details for this trip are not available…", with "Try again" (the same address) and the way back.</summary>
    public static PageAlert UnavailableAlert(string sameAddress, string backText, string backAddress)
    {
        var alert = PageAlert.Danger(Messages.ProblemSeatDetailsUnavailable);
        alert = alert.WithLink(Messages.LinkTryAgain, sameAddress);
        return alert.WithLink(backText, backAddress);
    }

    /// <summary>
    /// The seats laid out for drawing: the lower deck, then the upper deck when the trip has one,
    /// each as rows of places from row 1 and column 1, with an empty place where no seat is.
    /// </summary>
    public static IReadOnlyList<DeckLayout> ToGrid(IReadOnlyList<Seat> seats)
    {
        var decks = new List<DeckLayout>();
        foreach (string deck in new[] { LowerDeck, UpperDeck })
        {
            var deckSeats = seats.Where(seat => seat.Deck == deck).ToList();
            if (deckSeats.Count > 0)
            {
                decks.Add(ToDeck(deck, deckSeats));
            }
        }

        return decks;
    }

    /// <summary>
    /// The seat numbers a refusal names in one of its extension members (unavailableSeats,
    /// unknownSeats, seatNos), in the order given; empty when the member is missing.
    /// </summary>
    public static List<string> ExtensionSeats(ApiProblem problem, string name)
    {
        var seatNos = new List<string>();
        if (!problem.Extensions.TryGetValue(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return seatNos;
        }

        foreach (JsonElement seatNo in value.EnumerateArray())
        {
            seatNos.Add(seatNo.ToString());
        }

        return seatNos;
    }

    private static DeckLayout ToDeck(string deck, List<Seat> seats)
    {
        int rowCount = seats.Max(seat => seat.Row);
        int columnCount = seats.Max(seat => seat.Column);
        var rows = new List<Seat?[]>();
        for (int row = 0; row < rowCount; row++)
        {
            rows.Add(new Seat?[columnCount]);
        }

        foreach (Seat seat in seats)
        {
            rows[seat.Row - 1][seat.Column - 1] = seat;
        }

        return new DeckLayout(deck, Messages.DeckLabel(deck), rows);
    }

    /// <summary>One seat as a seat page shows it.</summary>
    public sealed record Seat
    {
        public required string SeatNo { get; init; }

        /// <summary>lower or upper.</summary>
        public required string Deck { get; init; }

        /// <summary>The seat's row on its deck, from 1.</summary>
        public required int Row { get; init; }

        /// <summary>The seat's column on its deck, from 1.</summary>
        public required int Column { get; init; }

        public required string SeatClass { get; init; }

        /// <summary>Today's fare in whole rupees.</summary>
        public required int Fare { get; init; }

        /// <summary>available, held, booked or blocked.</summary>
        public required string Status { get; init; }

        /// <summary>The facts a page adds under the status, such as "Blocked by: Operator".</summary>
        public IReadOnlyList<SeatLine> Lines { get; init; } = Array.Empty<SeatLine>();

        /// <summary>A mark that makes the seat stand out, such as a stuck hold's; null when none.</summary>
        public string? Warning { get; init; }

        /// <summary>
        /// The form field the seat is posted as when ticked; null when it cannot be picked. A page
        /// gives seats of different kinds different fields, so a post says what each seat was when shown.
        /// </summary>
        public string? PickField { get; init; }

        public bool IsPicked { get; init; }
    }

    /// <summary>One fact under a seat's status: "Booking ID: …", optionally a link to where the value is explained.</summary>
    public sealed record SeatLine(string Label, string Value, string? Href = null);

    /// <summary>One deck's places, row by row; a null place has no seat.</summary>
    public sealed record DeckLayout(string Deck, string Name, IReadOnlyList<Seat?[]> Rows);
}
