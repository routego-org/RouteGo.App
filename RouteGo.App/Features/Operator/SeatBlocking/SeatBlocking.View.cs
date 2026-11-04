namespace RouteGo.App.Features.Operator.SeatBlocking;

/// <summary>How one of the operator's seats is drawn: its "Blocked by" line, and whether and as what it can be picked.</summary>
public static class TripSeatView
{
    /// <summary>
    /// The field the seat is posted as when ticked: Available seats and the operator's own blocks,
    /// while the trip can still change. Held seats, booked seats and seats blocked by the trip's
    /// cancellation can never be picked (null).
    /// </summary>
    public static string? PickField(TripSeat seat, bool canAct)
    {
        if (!canAct)
        {
            return null;
        }

        if (seat.Status == SeatDisplay.Available)
        {
            return SeatBlockingModel.AvailableField;
        }

        if (seat.Status == SeatDisplay.Blocked && seat.BlockReason == SeatDisplay.BlockedByOperator)
        {
            return SeatBlockingModel.BlockedField;
        }

        return null;
    }

    public static SeatDisplay.Seat ToSeat(TripSeat seat, string? pickField, bool isPicked)
    {
        var lines = new List<SeatDisplay.SeatLine>();
        if (seat.Status == SeatDisplay.Blocked && seat.BlockReason != null)
        {
            lines.Add(SeatDisplay.BlockedByLine(seat.BlockReason));
        }

        return new SeatDisplay.Seat
        {
            SeatNo = seat.SeatNo,
            Deck = seat.Deck,
            Row = seat.Row,
            Column = seat.Column,
            SeatClass = seat.SeatClass,
            Fare = seat.Fare,
            Status = seat.Status,
            Lines = lines,
            PickField = pickField,
            IsPicked = isPicked,
        };
    }
}
