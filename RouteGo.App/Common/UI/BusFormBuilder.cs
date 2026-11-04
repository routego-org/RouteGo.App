using System.Globalization;

namespace RouteGo.App.Common.UI;

/// <summary>
/// The seat layout builder's steps, shared by the add-a-bus and change-a-bus pages. Each builder
/// button posts the whole form with a step such as "fill:lower" or "remove-seat:12"; nothing is
/// sent to Fleet until "Save bus".
/// </summary>
public static class BusFormBuilder
{
    public const string AddSeatClassAction = "add-class";

    private const string RedrawAction = "redraw";
    private const string RemoveUpperDeckAction = "remove-upper";
    private const string KeepUpperDeckAction = "keep-upper";
    private const char ActionSeparator = ':';
    private const string ColumnLetters = "ABCDEF";
    private const string UpperDeckPrefix = "U";

    /// <summary>
    /// The name a place gets when a deck is filled or a seat is added there: row number and column
    /// letter on the lower deck ("1A"), with "U" in front on the upper deck ("U1A").
    /// </summary>
    public static string SeatName(string deck, int row, int column)
    {
        string name = row.ToString(CultureInfo.InvariantCulture) + ColumnLetters[column - 1];
        if (deck == BusFormInput.UpperDeck)
        {
            return UpperDeckPrefix + name;
        }

        return name;
    }

    public static string FillAction(string deck)
    {
        return "fill:" + deck;
    }

    public static string AddSeatAction(string deck, int row, int column)
    {
        return $"add-seat:{deck}:{row}:{column}";
    }

    public static string RemoveSeatAction(int index)
    {
        return "remove-seat:" + index.ToString(CultureInfo.InvariantCulture);
    }

    public static string DeckClassAction(string deck)
    {
        return "deck-class:" + deck;
    }

    public static string RemoveSeatClassAction(int index)
    {
        return "remove-class:" + index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>"Seats: 36 of 60".</summary>
    public static string SeatCountText(int count)
    {
        return $"Seats: {count} of {BusFormInput.MaxSeats}";
    }

    /// <summary>"Lower berth: 18 seats".</summary>
    public static string SeatClassCountText(string seatClass, int count)
    {
        return $"{seatClass}: {count} seats";
    }

    public static string TooManySeatsText(int count)
    {
        int extra = count - BusFormInput.MaxSeats;
        return $"A bus can have at most {BusFormInput.MaxSeats} seats. You have {count}. Remove {extra} seats.";
    }

    /// <summary>Prepares the form for "Save bus": the same as a builder step that changes nothing.</summary>
    public static BusFormOutcome PrepareSave(BusFormInput input)
    {
        return Apply(input, RedrawAction);
    }

    /// <summary>
    /// Runs one builder step on the posted form, after bringing it up to date: renamed classes on
    /// their seats, and seats outside a smaller deck removed (and said so).
    /// </summary>
    public static BusFormOutcome Apply(BusFormInput input, string? action)
    {
        var outcome = new BusFormOutcome();
        input.EnsureOneSeatClass();
        input.ApplySeatClassRenames();
        int removed = input.RemoveSeatsOutside();
        if (removed > 0)
        {
            outcome.Notice = $"{removed} seats outside the new size were removed.";
        }

        string[] parts = (action ?? RedrawAction).Split(ActionSeparator);
        bool settlesUpperDeck = RunStep(input, parts, outcome);
        if (!settlesUpperDeck)
        {
            AskAboutUpperDeck(input, outcome);
        }

        input.RememberSeatClasses();
        return outcome;
    }

    // Returns whether the step itself settled the upper deck (removed it or kept it).
    private static bool RunStep(BusFormInput input, string[] parts, BusFormOutcome outcome)
    {
        switch (parts[0])
        {
            case RemoveUpperDeckAction:
                input.RemoveUpperDeckSeats();
                input.HasUpperDeck = false;
                return true;
            case KeepUpperDeckAction:
                input.HasUpperDeck = true;
                return true;
            case AddSeatClassAction:
                input.AddSeatClass();
                return false;
            case "remove-class":
                RemoveSeatClass(input, NumberAt(parts, 1), outcome);
                return false;
            default:
                RunSeatStep(input, parts, outcome);
                return false;
        }
    }

    // The steps that put seats on a deck or change them; an unknown step changes nothing.
    private static void RunSeatStep(BusFormInput input, string[] parts, BusFormOutcome outcome)
    {
        switch (parts[0])
        {
            case "fill":
                Fill(input, DeckOf(parts), false, outcome);
                break;
            case "refill":
                Fill(input, DeckOf(parts), true, outcome);
                break;
            case "add-seat":
                input.AddSeat(DeckOf(parts), NumberAt(parts, 2), NumberAt(parts, 3));
                break;
            case "remove-seat":
                input.RemoveSeat(NumberAt(parts, 1));
                break;
            case "deck-class":
                input.SetDeckClass(DeckOf(parts));
                break;
        }
    }

    private static void Fill(BusFormInput input, string deck, bool isConfirmed, BusFormOutcome outcome)
    {
        BusFormDeck size = input.Deck(deck);
        string field = DeckField(deck);
        if (size.Rows is null)
        {
            outcome.Problems.Add(new BusFormProblem(field + ".Rows", Messages.ValidationRequired("Rows")));
        }

        if (size.Columns is null)
        {
            outcome.Problems.Add(new BusFormProblem(field + ".Columns", Messages.ValidationRequired("Columns")));
        }

        if (outcome.Problems.Count > 0)
        {
            return;
        }

        if (!isConfirmed && input.SeatCount(deck) > 0)
        {
            outcome.Question = RefillQuestion(deck);
            return;
        }

        input.Fill(deck);
        if (deck == BusFormInput.UpperDeck)
        {
            input.HasUpperDeck = true;
        }
    }

    private static BusFormQuestion RefillQuestion(string deck)
    {
        string deckName = Messages.DeckLabel(deck).ToLowerInvariant();
        return new BusFormQuestion
        {
            Text = $"Replace all seats on the {deckName}? Your changes to these seats will be lost.",
            ConfirmText = "Replace seats",
            ConfirmAction = "refill:" + deck,
            CancelAction = RedrawAction,
        };
    }

    private static void RemoveSeatClass(BusFormInput input, int index, BusFormOutcome outcome)
    {
        if (index < 0 || index >= input.SeatClasses.Count)
        {
            return;
        }

        string? seatClass = input.SeatClasses[index];
        int seats = string.IsNullOrEmpty(seatClass) ? 0 : input.SeatsWithClass(seatClass);
        if (seats > 0)
        {
            outcome.Problems.Add(new BusFormProblem($"SeatClasses[{index}]", $"{seatClass} still has {seats} seats. Change their class first."));
            return;
        }

        input.RemoveSeatClass(index);
    }

    // Unticking "This bus has an upper deck" takes effect only once the operator agrees to lose its seats.
    private static void AskAboutUpperDeck(BusFormInput input, BusFormOutcome outcome)
    {
        int upperSeats = input.SeatCount(BusFormInput.UpperDeck);
        if (input.HasUpperDeck || upperSeats == 0 || outcome.Question != null)
        {
            return;
        }

        outcome.Question = new BusFormQuestion
        {
            Text = $"Remove the upper deck and its {upperSeats} seats?",
            ConfirmText = "Remove upper deck",
            ConfirmAction = RemoveUpperDeckAction,
            CancelAction = KeepUpperDeckAction,
        };
    }

    // The deck a step names; anything but "upper" is the lower deck, which every bus has.
    private static string DeckOf(string[] parts)
    {
        if (parts.Length > 1 && parts[1] == BusFormInput.UpperDeck)
        {
            return BusFormInput.UpperDeck;
        }

        return BusFormInput.LowerDeck;
    }

    // A step's number, or -1 when it is missing or not a number (the step then changes nothing).
    private static int NumberAt(string[] parts, int position)
    {
        if (parts.Length <= position)
        {
            return -1;
        }

        bool isNumber = int.TryParse(parts[position], NumberStyles.None, CultureInfo.InvariantCulture, out int number);
        if (!isNumber)
        {
            return -1;
        }

        return number;
    }

    private static string DeckField(string deck)
    {
        if (deck == BusFormInput.UpperDeck)
        {
            return nameof(BusFormInput.Upper);
        }

        return nameof(BusFormInput.Lower);
    }
}
