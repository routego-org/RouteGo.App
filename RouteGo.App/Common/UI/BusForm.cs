namespace RouteGo.App.Common.UI;

/// <summary>One seat as the bus form holds it: its seat number, place and seat class.</summary>
public sealed class BusFormSeat   // mutable class: must round-trip on re-render
{
    public string? Label { get; set; }

    /// <summary>lower or upper, as Fleet spells the decks.</summary>
    public string Deck { get; set; } = BusFormInput.LowerDeck;

    public int Row { get; set; }

    public int Column { get; set; }

    public string? SeatClass { get; set; }
}

/// <summary>One deck's size and aisle as chosen on the form; null where nothing is chosen yet.</summary>
public sealed class BusFormDeck   // mutable class: must round-trip on re-render
{
    public int? Rows { get; set; }

    public int? Columns { get; set; }

    /// <summary>The column the aisle runs after; null for "No aisle".</summary>
    public int? AisleAfterColumn { get; set; }

    /// <summary>The seat class "Use for the whole deck" gives every seat on the deck.</summary>
    public string? ClassForAll { get; set; }

    /// <summary>Whether the aisle can be drawn: after a column that leaves at least one column beyond it.</summary>
    public bool HasAisleWithin()
    {
        return AisleAfterColumn != null && Columns != null && AisleAfterColumn < Columns;
    }
}

/// <summary>
/// Everything the add-a-bus and change-a-bus pages post: the bus's details, its seat classes, both
/// decks and every seat. The page keeps nothing between requests, so each post carries the whole
/// form and the builder's steps (fill, add, remove, rename) work on it here.
/// </summary>
public sealed class BusFormInput   // mutable class: must round-trip on re-render
{
    public const string LowerDeck = "lower";
    public const string UpperDeck = "upper";
    public const int MaxSeats = 60;
    public const int MaxSeatClasses = 5;
    public const int MaxRows = 15;
    public const int MaxColumns = 6;

    /// <summary>Fleet's bus types, in the order the form offers them.</summary>
    public static readonly string[] BusTypeChoices = new[] { "seater", "sleeper", "semiSleeper" };

    /// <summary>Fleet's amenities, in the order the form offers them.</summary>
    public static readonly string[] AmenityChoices = new[] { "wifi", "chargingPoint", "blanket", "waterBottle", "readingLight", "cctv", "gpsTracking", "restroom" };

    public string? Name { get; set; }

    public string? RegistrationNumber { get; set; }

    /// <summary>seater, sleeper or semiSleeper.</summary>
    public string? BusType { get; set; }

    /// <summary>Null until Yes or No is chosen.</summary>
    public bool? IsAc { get; set; }

    public List<string> Amenities { get; set; } = new List<string>();

    public List<string?> SeatClasses { get; set; } = new List<string?>();

    /// <summary>
    /// The seat class names as the page last drew them, so a renamed class can be renamed on every
    /// seat that has it.
    /// </summary>
    public List<string?> SeatClassesBefore { get; set; } = new List<string?>();

    public bool HasUpperDeck { get; set; }

    public BusFormDeck Lower { get; set; } = new BusFormDeck();

    public BusFormDeck Upper { get; set; } = new BusFormDeck();

    public List<BusFormSeat> Seats { get; set; } = new List<BusFormSeat>();

    public BusFormDeck Deck(string deck)
    {
        if (deck == UpperDeck)
        {
            return Upper;
        }

        return Lower;
    }

    /// <summary>A new form starts with one empty seat class to name.</summary>
    public void EnsureOneSeatClass()
    {
        if (SeatClasses.Count == 0)
        {
            SeatClasses.Add(string.Empty);
        }
    }

    /// <summary>Remembers the class names as drawn, to spot a rename on the next post.</summary>
    public void RememberSeatClasses()
    {
        SeatClassesBefore = new List<string?>(SeatClasses);
    }

    /// <summary>The first seat class in the list, which filled and added seats get.</summary>
    public string FirstSeatClass()
    {
        if (SeatClasses.Count == 0)
        {
            return string.Empty;
        }

        return SeatClasses[0] ?? string.Empty;
    }

    /// <summary>The seat class names that can be chosen for a seat: every non-empty one, in order.</summary>
    public List<string> NamedSeatClasses()
    {
        var named = new List<string>();
        foreach (string? seatClass in SeatClasses)
        {
            if (!string.IsNullOrWhiteSpace(seatClass))
            {
                named.Add(seatClass);
            }
        }

        return named;
    }

    public int SeatCount(string deck)
    {
        return Seats.Count(seat => seat.Deck == deck);
    }

    /// <summary>How many seats have the class, ignoring capitals.</summary>
    public int SeatsWithClass(string seatClass)
    {
        return Seats.Count(seat => string.Equals(seat.SeatClass, seatClass, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The index of the seat on the place, or -1 when the place is empty.</summary>
    public int SeatIndexAt(string deck, int row, int column)
    {
        return Seats.FindIndex(seat => seat.Deck == deck && seat.Row == row && seat.Column == column);
    }

    /// <summary>Replaces the deck's seats with one seat on every place, each with the first seat class.</summary>
    public void Fill(string deck)
    {
        BusFormDeck size = Deck(deck);
        Seats.RemoveAll(seat => seat.Deck == deck);
        for (int row = 1; row <= size.Rows; row++)
        {
            for (int column = 1; column <= size.Columns; column++)
            {
                Seats.Add(NewSeat(deck, row, column));
            }
        }
    }

    /// <summary>Puts a seat on an empty place inside the deck; anything else is ignored.</summary>
    public void AddSeat(string deck, int row, int column)
    {
        BusFormDeck size = Deck(deck);
        bool isInside = row >= 1 && row <= size.Rows && column >= 1 && column <= size.Columns;
        if (!isInside || SeatIndexAt(deck, row, column) >= 0)
        {
            return;
        }

        Seats.Add(NewSeat(deck, row, column));
    }

    public void RemoveSeat(int index)
    {
        if (index < 0 || index >= Seats.Count)
        {
            return;
        }

        Seats.RemoveAt(index);
    }

    /// <summary>Gives every seat on the deck the class chosen for the whole deck.</summary>
    public void SetDeckClass(string deck)
    {
        string? seatClass = Deck(deck).ClassForAll;
        foreach (BusFormSeat seat in Seats)
        {
            if (seat.Deck == deck)
            {
                seat.SeatClass = seatClass;
            }
        }
    }

    public bool CanAddSeatClass()
    {
        return SeatClasses.Count < MaxSeatClasses;
    }

    public void AddSeatClass()
    {
        if (!CanAddSeatClass())
        {
            return;
        }

        SeatClasses.Add(string.Empty);
    }

    /// <summary>A bus keeps at least one seat class, so the last one cannot be removed.</summary>
    public bool CanRemoveSeatClass()
    {
        return SeatClasses.Count > 1;
    }

    public void RemoveSeatClass(int index)
    {
        if (!CanRemoveSeatClass() || index < 0 || index >= SeatClasses.Count)
        {
            return;
        }

        SeatClasses.RemoveAt(index);
    }

    /// <summary>Gives every seat of a renamed class the class's new name.</summary>
    public void ApplySeatClassRenames()
    {
        int count = Math.Min(SeatClasses.Count, SeatClassesBefore.Count);
        for (int index = 0; index < count; index++)
        {
            string? before = SeatClassesBefore[index];
            string? now = SeatClasses[index];
            if (string.IsNullOrEmpty(before) || string.Equals(before, now, StringComparison.Ordinal))
            {
                continue;
            }

            RenameSeatClass(before, now);
        }
    }

    /// <summary>Removes the seats a smaller deck no longer has a place for, and says how many.</summary>
    public int RemoveSeatsOutside()
    {
        return Seats.RemoveAll(IsOutsideItsDeck);
    }

    /// <summary>Removes the upper deck's seats, after the operator agreed.</summary>
    public void RemoveUpperDeckSeats()
    {
        Seats.RemoveAll(seat => seat.Deck == UpperDeck);
    }

    private BusFormSeat NewSeat(string deck, int row, int column)
    {
        return new BusFormSeat
        {
            Label = BusFormBuilder.SeatName(deck, row, column),
            Deck = deck,
            Row = row,
            Column = column,
            SeatClass = FirstSeatClass(),
        };
    }

    private void RenameSeatClass(string before, string? now)
    {
        foreach (BusFormSeat seat in Seats)
        {
            if (string.Equals(seat.SeatClass, before, StringComparison.OrdinalIgnoreCase))
            {
                seat.SeatClass = now;
            }
        }
    }

    // A deck whose rows or columns are not chosen keeps its seats: nothing says it became smaller.
    private bool IsOutsideItsDeck(BusFormSeat seat)
    {
        BusFormDeck size = Deck(seat.Deck);
        if (size.Rows is null || size.Columns is null)
        {
            return false;
        }

        return seat.Row > size.Rows || seat.Column > size.Columns;
    }
}
