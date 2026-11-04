using System.Globalization;

namespace RouteGo.App.Common.UI;

/// <summary>Where Fleet's field errors on a bus go on the bus form, in the form's own words.</summary>
public static class BusFormFleetErrors
{
    /// <summary>
    /// One problem per Fleet error; a field the form does not show as typed (a deck, the seat list as
    /// a whole) comes back with an empty field and Fleet's own message, for the top of the page.
    /// </summary>
    public static List<BusFormProblem> Place(IReadOnlyDictionary<string, string[]> errors, BusFormInput input)
    {
        var problems = new List<BusFormProblem>();
        foreach (KeyValuePair<string, string[]> error in errors)
        {
            problems.Add(PlaceOne(error.Key, error.Value, input));
        }

        return problems;
    }

    private static BusFormProblem PlaceOne(string key, string[] fleetMessages, BusFormInput input)
    {
        if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
        {
            return new BusFormProblem(nameof(BusFormInput.Name), Messages.ValidationFormat("bus name"));
        }

        if (string.Equals(key, "registrationNumber", StringComparison.OrdinalIgnoreCase))
        {
            return new BusFormProblem(nameof(BusFormInput.RegistrationNumber), BusFormValidator.RegistrationMessage);
        }

        int seatClass = IndexIn(key, "seatClasses[", "]", input.SeatClasses.Count);
        if (seatClass >= 0)
        {
            return new BusFormProblem($"SeatClasses[{seatClass}]", Messages.ValidationFormat("seat class"));
        }

        int seatLabel = IndexIn(key, "layout.seats[", "].label", input.Seats.Count);
        if (seatLabel >= 0)
        {
            return new BusFormProblem($"Seats[{seatLabel}].Label", BusFormValidator.SeatNumberMessage);
        }

        int seatOfClass = IndexIn(key, "layout.seats[", "].seatClass", input.Seats.Count);
        if (seatOfClass >= 0)
        {
            return new BusFormProblem($"Seats[{seatOfClass}].SeatClass", Messages.ValidationFormat("seat class"));
        }

        string fleetMessage = fleetMessages.Length > 0 ? fleetMessages[0] : string.Empty;
        return new BusFormProblem(string.Empty, fleetMessage);
    }

    // "layout.seats[3].label" with start "layout.seats[" and end "].label" -> 3, when the form has
    // that many items; -1 for any other key.
    private static int IndexIn(string key, string start, string end, int count)
    {
        bool matches = key.StartsWith(start, StringComparison.OrdinalIgnoreCase)
            && key.EndsWith(end, StringComparison.OrdinalIgnoreCase)
            && key.Length > start.Length + end.Length;
        if (!matches)
        {
            return -1;
        }

        string indexText = key.Substring(start.Length, key.Length - start.Length - end.Length);
        bool isNumber = int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out int index);
        if (!isNumber || index >= count)
        {
            return -1;
        }

        return index;
    }
}
