namespace RouteGo.App.Features.Operator.SeatBlocking;

/// <summary>
/// The page's own checks on the picked seats before anything is sent: something suitable must be
/// picked, and no more than Seat Inventory takes at once.
/// </summary>
public static class SeatPicks
{
    /// <summary>The most seats Seat Inventory blocks or unblocks in one request.</summary>
    public const int MaxSeats = 60;

    public const string NothingToBlock = "Pick at least one available seat to block.";
    public const string NothingToUnblock = "Pick at least one seat you blocked to unblock.";
    public const string TooMany = "You can block or unblock at most 60 seats at a time.";

    /// <summary>The picked seat numbers, each once, in the order posted.</summary>
    public static List<string> Distinct(IEnumerable<string> picks)
    {
        var seatNos = new List<string>();
        foreach (string pick in picks)
        {
            if (!string.IsNullOrWhiteSpace(pick) && !seatNos.Contains(pick))
            {
                seatNos.Add(pick);
            }
        }

        return seatNos;
    }

    /// <summary>Why the seats cannot be sent; null when they can.</summary>
    /// <param name="seatNos">The seats the action would send.</param>
    /// <param name="nothingPicked">What to say when there are none.</param>
    public static string? Check(IReadOnlyCollection<string> seatNos, string nothingPicked)
    {
        if (seatNos.Count == 0)
        {
            return nothingPicked;
        }

        if (seatNos.Count > MaxSeats)
        {
            return TooMany;
        }

        return null;
    }
}

/// <summary>
/// The spec's words for Seat Inventory's refusals of a block or unblock. Each refusal names the
/// seats at fault, because Seat Inventory changes all the seats sent or none.
/// </summary>
public static class SeatRefusals
{
    private const string SeatsUnavailable = "SEATS_UNAVAILABLE";
    private const string SeatsNotOperatorBlocked = "SEATS_NOT_OPERATOR_BLOCKED";
    private const string UnknownSeats = "UNKNOWN_SEATS";
    private const string TripCancelled = "TRIP_CANCELLED";
    private const string TripDeparted = "TRIP_DEPARTED";

    /// <summary>The alert for a refused block or unblock, shown above the seats loaded again.</summary>
    public static PageAlert ToAlert<T>(ApiResult<T> api, string correlationId)
    {
        if (api.Errors != null)
        {
            return PageAlert.Danger(api.ErrorsTitle ?? Messages.ProblemInternalError(correlationId));
        }

        ApiProblem problem = api.Problem!;
        switch (problem.Code)
        {
            case SeatsUnavailable:
                string unavailable = Seats(problem, "unavailableSeats");
                return PageAlert.Danger($"Some seats are no longer free: {unavailable}. Nothing was blocked. Pick the seats again.");
            case SeatsNotOperatorBlocked:
                string notBlocked = Seats(problem, "seatNos");
                return PageAlert.Danger($"These seats are no longer blocked by you: {notBlocked}. Nothing was unblocked.");
            case UnknownSeats:
                string unknown = Seats(problem, "unknownSeats");
                return PageAlert.Danger($"Some seats are not on this trip: {unknown}. Nothing was changed.");
            case TripCancelled:
                return PageAlert.Danger(Messages.ProblemTripCancelled);
            case TripDeparted:
                return PageAlert.Danger(Messages.ProblemTripDeparted);
            default:
                return SharedProblems.ToAlert(problem, correlationId);
        }
    }

    private static string Seats(ApiProblem problem, string extension)
    {
        List<string> seatNos = SeatDisplay.ExtensionSeats(problem, extension);
        return SeatDisplay.SeatList(seatNos);
    }
}
