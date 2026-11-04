namespace RouteGo.App.Features.Operator.Bus;

/// <summary>One row of a deck: a place for every column, empty where the deck has no seat.</summary>
public sealed record SeatGridRow(int Row, IReadOnlyList<BusSeat?> Places);

/// <summary>
/// One deck of the seat layout drawn as the operator built it: every row and column in order, so
/// the page can show empty places and the aisle exactly where they are.
/// </summary>
public sealed record SeatGridDeck(string Deck, IReadOnlyList<SeatGridRow> Rows)
{
    /// <summary>The column the aisle runs after; null when the deck has no aisle.</summary>
    public int? AisleAfterColumn { get; init; }

    /// <summary>Every deck of the layout in Fleet's order (lower first).</summary>
    public static IReadOnlyList<SeatGridDeck> Build(BusLayout layout)
    {
        var seatsByPlace = new Dictionary<string, BusSeat>();
        foreach (BusSeat seat in layout.Seats)
        {
            seatsByPlace[PlaceKey(seat.Deck, seat.Row, seat.Column)] = seat;
        }

        var decks = new List<SeatGridDeck>();
        foreach (BusDeck deck in layout.Decks)
        {
            decks.Add(BuildDeck(deck, seatsByPlace));
        }

        return decks;
    }

    private static SeatGridDeck BuildDeck(BusDeck deck, Dictionary<string, BusSeat> seatsByPlace)
    {
        var rows = new List<SeatGridRow>();
        for (int row = 1; row <= deck.Rows; row++)
        {
            rows.Add(BuildRow(deck, row, seatsByPlace));
        }

        return new SeatGridDeck(deck.Deck, rows) { AisleAfterColumn = deck.AisleAfterColumn };
    }

    private static SeatGridRow BuildRow(BusDeck deck, int row, Dictionary<string, BusSeat> seatsByPlace)
    {
        var places = new List<BusSeat?>();
        for (int column = 1; column <= deck.Columns; column++)
        {
            seatsByPlace.TryGetValue(PlaceKey(deck.Deck, row, column), out BusSeat? seat);
            places.Add(seat);
        }

        return new SeatGridRow(row, places);
    }

    private static string PlaceKey(string deck, int row, int column)
    {
        return $"{deck}/{row}/{column}";
    }
}
