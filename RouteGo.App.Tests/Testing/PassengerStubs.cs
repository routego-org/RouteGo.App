namespace RouteGo.App.Tests.Testing;

/// <summary>
/// Booking's passenger list of a trip: the specs' example, trip T-101 Mumbai → Pune with three
/// seats on two confirmed bookings, as Booking's TripPassengerList.
/// </summary>
public static class PassengerStubs
{
    public static readonly Guid TripId = BookingStubs.TripId;
    public static readonly Guid OtherBookingId = Guid.Parse("55555555-0000-4000-8000-000000000055");

    public static readonly string Path = $"/trips/{TripId}/passengers";
    public static readonly string PageAddress = $"/operator/trips/{TripId}/passengers";
    public static readonly string PageAddressWithRoute = $"{PageAddress}?route=Mumbai%20%E2%86%92%20Pune";

    /// <summary>The three seats in seat order, with the two bookings' ids and their passengers.</summary>
    public static HttpResponseMessage List()
    {
        object[] passengers = new object[]
        {
            Passenger("1C", OtherBookingId, "Sameer Rao", 29, "male"),
            Passenger("3A", BookingStubs.BookingId, "Priya Shah", 34, "female"),
            Passenger("3B", BookingStubs.BookingId, "Ravi Shah", 36, "male"),
        };
        return Answer(passengers.Length, passengers);
    }

    /// <summary>A passenger list with the seats and count given.</summary>
    public static HttpResponseMessage Answer(int seatsBooked, params object[] passengers)
    {
        return FakeBackend.Json(new { trip = new BookingStub().Trip(), seatsBooked, passengers });
    }

    public static object Passenger(string seatNo, Guid bookingId, string name, int age, string gender)
    {
        return new { seatNo, seatClass = "Seater", bookingId, passengerName = name, passengerAge = age, passengerGender = gender };
    }
}
