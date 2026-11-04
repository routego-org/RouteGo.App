using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Tests.Testing;

/// <summary>
/// What the booking page tests share: trip T1's public seat map (3A, 3B and 3C free at ₹800, 4A
/// booked), Priya's profile, the page's address, and the form a customer fills in.
/// </summary>
public static class BookSeatsStubs
{
    public const string CheckoutUrl = "https://pay.test/checkout/b-90";

    public static readonly string SeatMapPath = $"/trips/{BookingStubs.TripId}/seats";
    public static readonly string RoutedAddress = $"/trips/{BookingStubs.TripId}/book?route=Mumbai%20%E2%86%92%20Pune";

    /// <summary>A host whose seat map and profile answer, ready for a customer to open the booking page.</summary>
    public static FleetTestHost Host(Func<HttpResponseMessage> seatMap)
    {
        return Host(seatMap, Profile);
    }

    /// <summary>A host with the profile answering as given (the first answer mapped for a path wins).</summary>
    public static FleetTestHost Host(Func<HttpResponseMessage> seatMap, Func<HttpResponseMessage> profile)
    {
        var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatMapPath, seatMap);
        host.Identity.Map(HttpMethod.Get, "/me", profile);
        return host;
    }

    /// <summary>Trip T1's seat map: the given seats Available at ₹800 (default 3A, 3B, 3C), plus 4A booked.</summary>
    public static HttpResponseMessage SeatMap(params string[] freeSeats)
    {
        return SeatMap(false, false, 800, freeSeats);
    }

    public static HttpResponseMessage SeatMap(bool isCancelled, bool isDeparted, int fareOf3A, params string[] freeSeats)
    {
        string[] free = freeSeats;
        if (free.Length == 0)
        {
            free = new[] { "3A", "3B", "3C" };
        }

        var seats = new List<object>();
        for (int index = 0; index < free.Length; index++)
        {
            int fare = 800;
            if (free[index] == "3A")
            {
                fare = fareOf3A;
            }

            seats.Add(new { seatNo = free[index], deck = "lower", row = 1 + (index / 4), column = 1 + (index % 4), seatClass = "Seater", fare, status = "available" });
        }

        seats.Add(new { seatNo = "4A", deck = "lower", row = 9, column = 1, seatClass = "Seater", fare = 800, status = "booked" });
        return FakeBackend.Json(new { tripId = BookingStubs.TripId, departureAt = "2026-10-02T15:30:00Z", isCancelled, isDeparted, seats });
    }

    /// <summary>The seat map after someone else booked a seat: it is Booked, the given seats are still free.</summary>
    public static HttpResponseMessage SeatMapWithBooked(string bookedSeat, params string[] freeSeats)
    {
        var seats = new List<object>();
        seats.Add(new { seatNo = bookedSeat, deck = "lower", row = 1, column = 1, seatClass = "Seater", fare = 800, status = "booked" });
        for (int index = 0; index < freeSeats.Length; index++)
        {
            seats.Add(new { seatNo = freeSeats[index], deck = "lower", row = 2, column = index + 1, seatClass = "Seater", fare = 800, status = "available" });
        }

        return FakeBackend.Json(new { tripId = BookingStubs.TripId, departureAt = "2026-10-02T15:30:00Z", isCancelled = false, isDeparted = false, seats });
    }

    /// <summary>Priya's profile as Identity gives it.</summary>
    public static HttpResponseMessage Profile()
    {
        return FakeBackend.Json(new { email = "priya@example.com", fullName = "Priya Shah", phone = (string?)null, role = "customer" });
    }

    /// <summary>A created booking with the payment page address (or none).</summary>
    public static HttpResponseMessage Created(string? checkoutUrl)
    {
        return FakeBackend.Json(new BookingStub { Status = "paymentPending", CheckoutUrl = checkoutUrl, HoldExpiresAt = "2026-10-02T10:10:00Z" }.ToWire(), HttpStatusCode.Created);
    }

    /// <summary>
    /// The filled-in form for the seats given: each seat's fare, Priya Shah (34, female) for the first
    /// and Ravi Shah (36, male) for the second, and the contact name.
    /// </summary>
    public static List<KeyValuePair<string, string>> Form(params string[] seats)
    {
        var fields = new List<KeyValuePair<string, string>>();
        fields.Add(Field("ContactName", "Priya Shah"));
        string[] names = new[] { "Priya Shah", "Ravi Shah", "Amit Rao" };
        string[] ages = new[] { "34", "36", "29" };
        string[] genders = new[] { "female", "male", "male" };
        for (int index = 0; index < seats.Length; index++)
        {
            fields.Add(Field("Picks", seats[index]));
            fields.Add(Field($"Passengers[{seats[index]}].Fare", "800"));
            fields.Add(Field($"Passengers[{seats[index]}].Name", names[index]));
            fields.Add(Field($"Passengers[{seats[index]}].Age", ages[index]));
            fields.Add(Field($"Passengers[{seats[index]}].Gender", genders[index]));
        }

        return fields;
    }

    public static KeyValuePair<string, string> Field(string name, string value)
    {
        return new KeyValuePair<string, string>(name, value);
    }

    /// <summary>The same form with one field's value changed.</summary>
    public static List<KeyValuePair<string, string>> With(List<KeyValuePair<string, string>> fields, string name, string value)
    {
        var changed = new List<KeyValuePair<string, string>>();
        foreach (KeyValuePair<string, string> field in fields)
        {
            if (field.Key == name)
            {
                changed.Add(Field(name, value));
            }
            else
            {
                changed.Add(field);
            }
        }

        return changed;
    }

    /// <summary>The seats whose tick box is checked on the drawn page, in page order.</summary>
    public static string[] Chosen(IDocument page)
    {
        return page.QuerySelectorAll("[data-pick][checked]").Select(box => box.GetAttribute("value")!).ToArray();
    }

    /// <summary>The seats whose passenger box is open on the drawn page.</summary>
    public static string[] OpenPassengers(IDocument page)
    {
        return page.QuerySelectorAll("[data-passenger]:not([hidden])").Select(box => box.GetAttribute("data-passenger")!).ToArray();
    }

    /// <summary>The booking requests sent to Booking.</summary>
    public static int BookingPosts(FleetTestHost host)
    {
        return host.Booking.Requests.Count(request => request.Method == HttpMethod.Post);
    }

    /// <summary>The error shown under a field, or empty.</summary>
    public static string ErrorOf(IDocument page, string key)
    {
        IElement? error = page.QuerySelector($"[data-error='{key}']");
        if (error is null)
        {
            return string.Empty;
        }

        return error.TextContent.Trim();
    }
}
