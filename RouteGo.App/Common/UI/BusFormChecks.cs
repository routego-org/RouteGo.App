namespace RouteGo.App.Common.UI;

/// <summary>
/// Fleet's limits for a bus, checked before "Save bus" so every mistake is shown at its field or
/// seat at once and nothing is sent until the form is right.
/// </summary>
public sealed class BusFormValidator : AbstractValidator<BusFormInput>
{
    public const string RegistrationMessage = "Registration number must have 6 to 15 letters and numbers. Spaces and hyphens are allowed.";
    public const string RegistrationTakenMessage = "This registration number is already registered with RouteGo. If this bus is yours, contact RouteGo support.";
    public const string SeatNumberMessage = "Seat number must be 1 to 5 letters or numbers, like 1A or U12.";
    public const string NoSeatsMessage = "Add at least one seat.";
    public const string SeatClassLabel = "Seat class";

    private const int MinNameLength = 2;
    private const int MaxNameLength = 80;
    private const int MinRegistrationCharacters = 6;
    private const int MaxRegistrationCharacters = 15;
    private const int MaxRegistrationLength = 30;
    private const int MinSeatClassLength = 2;
    private const int MaxSeatClassLength = 30;
    private const int MaxSeatNumberLength = 5;

    public BusFormValidator()
    {
        RuleFor(x => x.Name).Custom(CheckName);
        RuleFor(x => x.RegistrationNumber).Custom(CheckRegistration);
        RuleFor(x => x.BusType).Must(IsBusType).WithMessage(Messages.ValidationRequired("Bus type"));
        RuleFor(x => x.IsAc).NotNull().WithMessage(Messages.ValidationRequired("AC"));
        RuleFor(x => x).Custom(CheckSeatClasses);
        RuleFor(x => x).Custom(CheckDecks);
        RuleFor(x => x).Custom(CheckSeats);
    }

    private static void CheckName(string? name, ValidationContext<BusFormInput> context)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            context.AddFailure(nameof(BusFormInput.Name), Messages.ValidationRequired("Bus name"));
            return;
        }

        int length = name.Trim().Length;
        if (length < MinNameLength)
        {
            context.AddFailure(nameof(BusFormInput.Name), Messages.ValidationMinLength("Bus name", MinNameLength));
        }

        if (length > MaxNameLength)
        {
            context.AddFailure(nameof(BusFormInput.Name), Messages.ValidationMaxLength("Bus name", MaxNameLength));
        }
    }

    private static void CheckRegistration(string? registration, ValidationContext<BusFormInput> context)
    {
        if (string.IsNullOrWhiteSpace(registration))
        {
            context.AddFailure(nameof(BusFormInput.RegistrationNumber), Messages.ValidationRequired("Registration number"));
            return;
        }

        if (!IsRegistration(registration))
        {
            context.AddFailure(nameof(BusFormInput.RegistrationNumber), RegistrationMessage);
        }
    }

    // Letters, digits, spaces and hyphens; 6 to 15 letters and digits once spaces and hyphens are out.
    private static bool IsRegistration(string registration)
    {
        if (registration.Length > MaxRegistrationLength)
        {
            return false;
        }

        int lettersAndDigits = 0;
        foreach (char character in registration)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                lettersAndDigits++;
            }
            else if (character != ' ' && character != '-')
            {
                return false;
            }
        }

        return lettersAndDigits >= MinRegistrationCharacters && lettersAndDigits <= MaxRegistrationCharacters;
    }

    private static bool IsBusType(string? busType)
    {
        return busType != null && BusFormInput.BusTypeChoices.Contains(busType);
    }

    private static void CheckSeatClasses(BusFormInput input, ValidationContext<BusFormInput> context)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < input.SeatClasses.Count; index++)
        {
            string? problem = SeatClassProblem(input, input.SeatClasses[index], seen);
            if (problem != null)
            {
                context.AddFailure($"{nameof(BusFormInput.SeatClasses)}[{index}]", problem);
            }
        }
    }

    // The first class with a name keeps it; every later one with the same name is told.
    private static string? SeatClassProblem(BusFormInput input, string? seatClass, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(seatClass))
        {
            return Messages.ValidationRequired(SeatClassLabel);
        }

        string name = seatClass.Trim();
        if (name.Length < MinSeatClassLength)
        {
            return Messages.ValidationMinLength(SeatClassLabel, MinSeatClassLength);
        }

        if (name.Length > MaxSeatClassLength)
        {
            return Messages.ValidationMaxLength(SeatClassLabel, MaxSeatClassLength);
        }

        if (!seen.Add(name))
        {
            return $"Seat class {name} is listed twice.";
        }

        // With no seats at all, "Add at least one seat." says it once instead of at every class.
        if (input.Seats.Count > 0 && input.SeatsWithClass(seatClass) == 0)
        {
            return $"{name} has no seats. Give it at least one seat, or remove it.";
        }

        return null;
    }

    private static void CheckDecks(BusFormInput input, ValidationContext<BusFormInput> context)
    {
        CheckDeck(input.Lower, nameof(BusFormInput.Lower), context);
        if (input.HasUpperDeck)
        {
            CheckDeck(input.Upper, nameof(BusFormInput.Upper), context);
        }
    }

    private static void CheckDeck(BusFormDeck deck, string field, ValidationContext<BusFormInput> context)
    {
        if (deck.Rows is null)
        {
            context.AddFailure(field + "." + nameof(BusFormDeck.Rows), Messages.ValidationRequired("Rows"));
        }

        if (deck.Columns is null)
        {
            context.AddFailure(field + "." + nameof(BusFormDeck.Columns), Messages.ValidationRequired("Columns"));
            return;
        }

        if (deck.AisleAfterColumn is null || deck.HasAisleWithin())
        {
            return;
        }

        string aisleField = field + "." + nameof(BusFormDeck.AisleAfterColumn);
        if (deck.Columns.Value < 2)
        {
            context.AddFailure(aisleField, Messages.ValidationFormat("aisle after column"));
            return;
        }

        context.AddFailure(aisleField, Messages.ValidationRange("Aisle after column", 1, deck.Columns.Value - 1));
    }

    private static void CheckSeats(BusFormInput input, ValidationContext<BusFormInput> context)
    {
        if (input.Seats.Count == 0)
        {
            context.AddFailure(nameof(BusFormInput.Seats), NoSeatsMessage);
            return;
        }

        if (input.Seats.Count > BusFormInput.MaxSeats)
        {
            context.AddFailure(nameof(BusFormInput.Seats), BusFormBuilder.TooManySeatsText(input.Seats.Count));
        }

        Dictionary<string, int> labelCounts = CountLabels(input.Seats);
        List<string> seatClasses = input.NamedSeatClasses();
        for (int index = 0; index < input.Seats.Count; index++)
        {
            CheckSeat(input.Seats[index], index, labelCounts, seatClasses, context);
        }
    }

    private static void CheckSeat(BusFormSeat seat, int index, Dictionary<string, int> labelCounts, List<string> seatClasses, ValidationContext<BusFormInput> context)
    {
        string seatField = $"{nameof(BusFormInput.Seats)}[{index}].";
        if (!IsSeatNumber(seat.Label))
        {
            context.AddFailure(seatField + nameof(BusFormSeat.Label), SeatNumberMessage);
        }
        else if (labelCounts[seat.Label!.ToUpperInvariant()] > 1)
        {
            context.AddFailure(seatField + nameof(BusFormSeat.Label), $"Seat number {seat.Label} is used twice.");
        }

        bool hasKnownClass = seatClasses.Contains(seat.SeatClass ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        if (!hasKnownClass)
        {
            context.AddFailure(seatField + nameof(BusFormSeat.SeatClass), Messages.ValidationRequired(SeatClassLabel));
        }
    }

    private static Dictionary<string, int> CountLabels(List<BusFormSeat> seats)
    {
        var counts = new Dictionary<string, int>();
        foreach (BusFormSeat seat in seats)
        {
            if (!IsSeatNumber(seat.Label))
            {
                continue;
            }

            string key = seat.Label!.ToUpperInvariant();
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
        }

        return counts;
    }

    private static bool IsSeatNumber(string? label)
    {
        if (string.IsNullOrEmpty(label) || label.Length > MaxSeatNumberLength)
        {
            return false;
        }

        return label.All(char.IsAsciiLetterOrDigit);
    }
}
