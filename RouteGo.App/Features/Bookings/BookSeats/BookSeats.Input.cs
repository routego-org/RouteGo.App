using System.Globalization;

namespace RouteGo.App.Features.Bookings.BookSeats;

/// <summary>What was typed for one chosen seat, kept as typed so a refused form comes back as it was.</summary>
public sealed class PassengerInput
{
    // The form binder turns an empty box into null; an empty box is "typed nothing", never a missing value.
    private string _name = string.Empty;
    private string _age = string.Empty;
    private string _gender = string.Empty;
    private string _fare = string.Empty;

    public string Name
    {
        get { return _name; }
        set { _name = value ?? string.Empty; }
    }

    /// <summary>The age as typed; read as a whole number only when the page checks it.</summary>
    public string Age
    {
        get { return _age; }
        set { _age = value ?? string.Empty; }
    }

    /// <summary>male, female or other; empty when none was chosen.</summary>
    public string Gender
    {
        get { return _gender; }
        set { _gender = value ?? string.Empty; }
    }

    /// <summary>The seat's fare in rupees as the page showed it, which is what the total the customer saw is made of.</summary>
    public string Fare
    {
        get { return _fare; }
        set { _fare = value ?? string.Empty; }
    }
}

/// <summary>
/// The page's own checks before anything is sent, with Booking's field rules: 1 to 6 seats, a
/// passenger name of 2 to 60 characters, an age that is a whole number from 0 to 120, a gender,
/// and a contact name of 2 to 60 characters. Errors are keyed by field so each shows under its box.
/// </summary>
public static class BookSeatsChecks
{
    public const string ContactKey = "contactName";
    public const string PageKey = "";
    public const string NoSeatChosen = "Choose at least one seat.";
    public const string TooManySeats = "You can book up to 6 seats.";

    private const int NameMinLength = 2;
    private const int NameMaxLength = 60;
    private const int AgeMax = 120;

    public const string ContactLabel = "Contact name";
    public const string NameLabel = "Passenger name";
    public const string AgeLabel = "Passenger age";
    public const string GenderLabel = "Passenger gender";

    private static readonly string[] _genders = new[] { "male", "female", "other" };

    /// <summary>The key a seat's field error is stored under: "3A|age".</summary>
    public static string Key(string seatNo, string field)
    {
        return $"{seatNo}|{field}";
    }

    /// <summary>The picked seats, each once, without empty entries, in the order posted.</summary>
    public static List<string> Distinct(IEnumerable<string> picks)
    {
        var seats = new List<string>();
        foreach (string pick in picks)
        {
            if (!string.IsNullOrWhiteSpace(pick) && !seats.Contains(pick))
            {
                seats.Add(pick);
            }
        }

        return seats;
    }

    /// <summary>Every field error of the typed form, by field key; empty when the form can be sent.</summary>
    public static Dictionary<string, string> Check(IReadOnlyList<string> seats, IReadOnlyDictionary<string, PassengerInput> passengers, string contactName)
    {
        var errors = new Dictionary<string, string>();
        if (seats.Count == 0)
        {
            errors[PageKey] = NoSeatChosen;
        }

        CheckName(errors, ContactKey, ContactLabel, contactName);
        foreach (string seatNo in seats)
        {
            PassengerInput passenger = PassengerOf(passengers, seatNo);
            CheckName(errors, Key(seatNo, "name"), NameLabel, passenger.Name);
            CheckAge(errors, Key(seatNo, "age"), passenger.Age);
            CheckGender(errors, Key(seatNo, "gender"), passenger.Gender);
        }

        return errors;
    }

    /// <summary>The typed values of a seat, or empty ones when nothing was typed for it.</summary>
    public static PassengerInput PassengerOf(IReadOnlyDictionary<string, PassengerInput> passengers, string seatNo)
    {
        if (passengers.TryGetValue(seatNo, out PassengerInput? passenger))
        {
            return passenger;
        }

        return new PassengerInput();
    }

    /// <summary>The fare of a seat as the page showed it; null when it was not posted or is not a whole number.</summary>
    public static int? FareOf(PassengerInput passenger)
    {
        bool isNumber = int.TryParse(passenger.Fare, NumberStyles.None, CultureInfo.InvariantCulture, out int fare);
        if (!isNumber)
        {
            return null;
        }

        return fare;
    }

    private static void CheckName(Dictionary<string, string> errors, string key, string label, string typed)
    {
        string name = typed.Trim();
        int length = name.EnumerateRunes().Count();
        if (length == 0)
        {
            errors[key] = Messages.ValidationRequired(label);
        }
        else if (length < NameMinLength)
        {
            errors[key] = Messages.ValidationMinLength(label, NameMinLength);
        }
        else if (length > NameMaxLength)
        {
            errors[key] = Messages.ValidationMaxLength(label, NameMaxLength);
        }
    }

    private static void CheckAge(Dictionary<string, string> errors, string key, string typed)
    {
        string age = typed.Trim();
        if (age.Length == 0)
        {
            errors[key] = Messages.ValidationRequired(AgeLabel);
            return;
        }

        bool isWholeNumber = int.TryParse(age, NumberStyles.None, CultureInfo.InvariantCulture, out int years);
        if (!isWholeNumber)
        {
            errors[key] = Messages.ValidationFormat("passenger age");
        }
        else if (years > AgeMax)
        {
            errors[key] = Messages.ValidationRange(AgeLabel, 0, AgeMax);
        }
    }

    private static void CheckGender(Dictionary<string, string> errors, string key, string typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            errors[key] = Messages.ValidationRequired(GenderLabel);
        }
        else if (!_genders.Contains(typed))
        {
            errors[key] = Messages.ValidationFormat("passenger gender");
        }
    }
}
