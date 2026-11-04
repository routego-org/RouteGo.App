namespace RouteGo.App.Common.UI;

/// <summary>A mistake to show at a field of the bus form ("Lower.Rows", "Seats[3].Label"); an empty field means the top of the page.</summary>
public sealed record BusFormProblem(string Field, string Message);

/// <summary>A question the bus form asks before a step that throws seats away.</summary>
public sealed record BusFormQuestion
{
    public required string Text { get; init; }

    public required string ConfirmText { get; init; }

    /// <summary>The builder step the confirm button posts.</summary>
    public required string ConfirmAction { get; init; }

    /// <summary>The builder step the cancel button posts; it keeps everything typed.</summary>
    public required string CancelAction { get; init; }
}

/// <summary>What a builder step left to show: a notice, a question, or mistakes at their fields.</summary>
public sealed class BusFormOutcome
{
    public string? Notice { get; set; }

    public BusFormQuestion? Question { get; set; }

    public List<BusFormProblem> Problems { get; } = new List<BusFormProblem>();

    /// <summary>Whether saving must wait: seats were just removed, or a question is open.</summary>
    public bool StopsSave
    {
        get { return Notice != null || Question != null; }
    }
}

/// <summary>What the shared bus form partial draws: the form, what the last step left, and where its buttons go.</summary>
public sealed class BusFormView
{
    public required BusFormInput Input { get; init; }

    public required BusFormOutcome Outcome { get; init; }

    /// <summary>Where "Save bus" posts.</summary>
    public required string SaveAddress { get; init; }

    /// <summary>Where every builder button posts, naming its step in BuilderAction.</summary>
    public required string BuildAddress { get; init; }

    public required string CancelAddress { get; init; }

    /// <summary>
    /// The bus's saved name when changing a bus, carried with every post so the page keeps its title
    /// while the name box is being edited; null when adding a bus.
    /// </summary>
    public string? SavedName { get; init; }
}

/// <summary>One deck as Fleet takes it.</summary>
public sealed record BusRequestDeck
{
    public required string Deck { get; init; }

    public required int Rows { get; init; }

    public required int Columns { get; init; }

    /// <summary>Null when the deck has no aisle; Fleet needs the field even then.</summary>
    public required int? AisleAfterColumn { get; init; }
}

/// <summary>One seat as Fleet takes it.</summary>
public sealed record BusRequestSeat
{
    public required string Label { get; init; }

    public required string Deck { get; init; }

    public required int Row { get; init; }

    public required int Column { get; init; }

    public required string SeatClass { get; init; }
}

public sealed record BusRequestLayout(IReadOnlyList<BusRequestDeck> Decks, IReadOnlyList<BusRequestSeat> Seats);

/// <summary>A bus as Fleet's add (createBus) and replace (replaceBus) take it: every detail, always.</summary>
public sealed record BusRequest
{
    public required string RegistrationNumber { get; init; }

    public required string Name { get; init; }

    public required string BusType { get; init; }

    public required bool IsAc { get; init; }

    public required IReadOnlyList<string> Amenities { get; init; }

    public required IReadOnlyList<string> SeatClasses { get; init; }

    public required BusRequestLayout Layout { get; init; }

    /// <summary>
    /// The form as Fleet takes it. Called only after the page's checks passed, so every field is
    /// filled. The seats keep the form's order, so Fleet's "layout.seats[3]" is the form's fourth seat.
    /// </summary>
    public static BusRequest From(BusFormInput input)
    {
        var decks = new List<BusRequestDeck>();
        decks.Add(ToDeck(BusFormInput.LowerDeck, input.Lower));
        if (input.HasUpperDeck)
        {
            decks.Add(ToDeck(BusFormInput.UpperDeck, input.Upper));
        }

        var seats = new List<BusRequestSeat>();
        foreach (BusFormSeat seat in input.Seats)
        {
            seats.Add(ToSeat(seat));
        }

        return new BusRequest
        {
            RegistrationNumber = input.RegistrationNumber!.Trim(),
            Name = input.Name!.Trim(),
            BusType = input.BusType!,
            IsAc = input.IsAc!.Value,
            Amenities = input.Amenities,
            SeatClasses = input.NamedSeatClasses(),
            Layout = new BusRequestLayout(decks, seats),
        };
    }

    private static BusRequestDeck ToDeck(string deck, BusFormDeck size)
    {
        return new BusRequestDeck
        {
            Deck = deck,
            Rows = size.Rows!.Value,
            Columns = size.Columns!.Value,
            AisleAfterColumn = size.AisleAfterColumn,
        };
    }

    private static BusRequestSeat ToSeat(BusFormSeat seat)
    {
        return new BusRequestSeat
        {
            Label = seat.Label!.Trim(),
            Deck = seat.Deck,
            Row = seat.Row,
            Column = seat.Column,
            SeatClass = seat.SeatClass!,
        };
    }
}
