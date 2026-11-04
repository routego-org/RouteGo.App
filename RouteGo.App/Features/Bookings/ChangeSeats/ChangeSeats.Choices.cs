namespace RouteGo.App.Features.Bookings.ChangeSeats;

/// <summary>
/// What each of the booking's seats may move to, and what is checked on the moves before they are
/// sent. A seat is offered only when it is free, of the same class as the seat it replaces, not the
/// booking's own, and not already picked for another of the booking's seats.
/// </summary>
public static class ChangeSeatsChoices
{
    public const string NothingChosen = "Choose a new seat for at least one of your seats.";
    public const string SameSeatTwice = "Choose a different new seat for each of your seats.";

    /// <summary>One choice per booking seat, keeping the picks that are still on offer.</summary>
    public static IReadOnlyList<SeatChoice> Build(BookingDetail booking, TripSeatMap map, IReadOnlyDictionary<string, string> picked)
    {
        var choices = new List<SeatChoice>();
        foreach (BookingSeatDetail seat in booking.Seats)
        {
            List<string> candidates = Candidates(seat, booking, map, picked);
            string chosen = PickedFor(seat.SeatNo, picked);
            if (!candidates.Contains(chosen))
            {
                chosen = string.Empty;
            }

            choices.Add(new SeatChoice { Seat = seat, Candidates = candidates, Chosen = chosen });
        }

        return choices;
    }

    /// <summary>
    /// The moves the person picked: a booking seat with a new seat that is not itself. A seat left
    /// without a new seat stays where it is, so it has no move.
    /// </summary>
    public static List<SeatMove> MovesFrom(IReadOnlyDictionary<string, string> picked)
    {
        var moves = new List<SeatMove>();
        foreach (KeyValuePair<string, string> pick in picked)
        {
            bool isMove = !string.IsNullOrWhiteSpace(pick.Value) && pick.Value != pick.Key;
            if (isMove)
            {
                moves.Add(new SeatMove(pick.Key, pick.Value));
            }
        }

        return moves;
    }

    /// <summary>The page's own check before sending: at least one move, and no new seat named twice; null when the moves are fine.</summary>
    public static string? Check(IReadOnlyList<SeatMove> moves)
    {
        if (moves.Count == 0)
        {
            return NothingChosen;
        }

        int distinct = moves.Select(move => move.ToSeatNo).Distinct().Count();
        if (distinct != moves.Count)
        {
            return SameSeatTwice;
        }

        return null;
    }

    /// <summary>"4C → 5C, 4D → 5D".</summary>
    public static string MovesText(IReadOnlyList<SeatMove> moves)
    {
        var parts = new List<string>();
        foreach (SeatMove move in moves)
        {
            parts.Add($"{move.FromSeatNo} → {move.ToSeatNo}");
        }

        return string.Join(", ", parts);
    }

    private static List<string> Candidates(BookingSeatDetail seat, BookingDetail booking, TripSeatMap map, IReadOnlyDictionary<string, string> picked)
    {
        var ownSeats = booking.Seats.Select(own => own.SeatNo).ToList();
        var candidates = new List<string>();
        foreach (FreeSeatCandidate free in map.Seats)
        {
            bool isFreeSameClass = free.Status == SeatDisplay.Available && free.SeatClass == seat.SeatClass;
            if (isFreeSameClass && !ownSeats.Contains(free.SeatNo) && !IsPickedForAnother(free.SeatNo, seat.SeatNo, picked))
            {
                candidates.Add(free.SeatNo);
            }
        }

        return candidates;
    }

    private static bool IsPickedForAnother(string candidate, string seatNo, IReadOnlyDictionary<string, string> picked)
    {
        foreach (KeyValuePair<string, string> pick in picked)
        {
            if (pick.Key != seatNo && pick.Value == candidate)
            {
                return true;
            }
        }

        return false;
    }

    private static string PickedFor(string seatNo, IReadOnlyDictionary<string, string> picked)
    {
        if (picked.TryGetValue(seatNo, out string? pick))
        {
            return pick;
        }

        return string.Empty;
    }
}
