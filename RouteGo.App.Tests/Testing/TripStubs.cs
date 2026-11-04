namespace RouteGo.App.Tests.Testing;

/// <summary>
/// One of the operator's schedules as Fleet holds it, changeable during a test: a stub lambda reads
/// it on every call, so setting Status after "Stop schedule" makes the next read answer the new state.
/// Defaults are the specs' example: Mumbai → Pune via Lonavala on "Shivneri Sleeper", 22:30 on
/// Monday, Wednesday and Friday, fares sent in the reverse of the bus's seat-class order.
/// </summary>
public sealed class ScheduleStub
{
    public Guid ScheduleId { get; set; } = TripStubs.ScheduleId;

    public Guid RouteId { get; set; } = TripStubs.RouteId;

    public Guid BusId { get; set; } = TripStubs.BusId;

    public string DepartureTime { get; set; } = "22:30";

    public string[] Days { get; set; } = new[] { "monday", "wednesday", "friday" };

    public object[] ClassFares { get; set; } = new object[]
    {
        new { seatClass = "Upper berth", fare = 1000 },
        new { seatClass = "Lower berth", fare = 1200 },
    };

    public string Status { get; set; } = "active";

    public string TripsGeneratedThrough { get; set; } = "2026-10-16";

    /// <summary>Fleet's Schedule shape.</summary>
    public object ToWire()
    {
        return new
        {
            scheduleId = ScheduleId,
            operatorId = TripStubs.OperatorId,
            routeId = RouteId,
            busId = BusId,
            departureTime = DepartureTime,
            daysOfWeek = Days,
            classFares = ClassFares,
            status = Status,
            tripsGeneratedThrough = TripsGeneratedThrough,
            createdAt = new DateTimeOffset(2026, 10, 1, 4, 30, 0, TimeSpan.Zero),
            updatedAt = new DateTimeOffset(2026, 10, 2, 5, 0, 0, TimeSpan.Zero),
        };
    }

    public HttpResponseMessage Answer()
    {
        return FakeBackend.Json(ToWire());
    }
}

/// <summary>Fleet answers the schedule and trip pages share: the example route and bus, and list pages.</summary>
public static class TripStubs
{
    public static readonly Guid OperatorId = Guid.Parse("0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");
    public static readonly Guid ScheduleId = Guid.Parse("7d6c5b4a-3e2f-4a1b-9c8d-7e6f5a4b3c2d");
    public static readonly Guid RouteId = Guid.Parse("4f1c2b3a-5d6e-4f70-8a9b-0c1d2e3f4a5b");
    public static readonly Guid BusId = Guid.Parse("0b7c6d5e-4f3a-4b2c-9d1e-8f7a6b5c4d3e");
    public static readonly Guid SecondBusId = Guid.Parse("1c8d7e6f-5a4b-4c3d-8e2f-9a8b7c6d5e4f");
    public static readonly Guid SecondRouteId = Guid.Parse("2d9e8f7a-6b5c-4d4e-9f3a-0b9c8d7e6f5a");

    /// <summary>The route Mumbai → Pune via Lonavala: stops at 0 min, 1 h 50 min and 3 h 30 min.</summary>
    public static object MumbaiPune(string status)
    {
        string[] cities = new[] { "Mumbai", "Lonavala", "Pune" };
        int[] offsets = new[] { 0, 110, 210 };
        return Route(RouteId, status, cities, offsets);
    }

    /// <summary>The route Pune → Nashik, with no stop in between.</summary>
    public static object PuneNashik(string status)
    {
        string[] cities = new[] { "Pune", "Nashik" };
        int[] offsets = new[] { 0, 240 };
        return Route(SecondRouteId, status, cities, offsets);
    }

    /// <summary>The bus "Shivneri Sleeper" with seat classes "Lower berth" then "Upper berth".</summary>
    public static object ShivneriSleeper(string status)
    {
        string[] seatClasses = new[] { "Lower berth", "Upper berth" };
        return Bus(BusId, "Shivneri Sleeper", status, seatClasses);
    }

    /// <summary>The bus "Volvo Seater" with the one seat class "Seater".</summary>
    public static object VolvoSeater(string status)
    {
        string[] seatClasses = new[] { "Seater" };
        return Bus(SecondBusId, "Volvo Seater", status, seatClasses);
    }

    /// <summary>One page of a Fleet list.</summary>
    public static HttpResponseMessage Page(int totalCount, int page, params object[] items)
    {
        return FakeBackend.Json(new { items, totalCount, page, pageSize = 20 });
    }

    // Fleet's Route shape.
    private static object Route(Guid routeId, string status, string[] cities, int[] offsets)
    {
        var stops = new List<object>();
        for (int index = 0; index < cities.Length; index++)
        {
            var city = new { cityId = Guid.NewGuid(), name = cities[index], state = "Maharashtra" };
            stops.Add(new { sequence = index + 1, city, offsetMinutes = offsets[index] });
        }

        return new
        {
            routeId,
            operatorId = OperatorId,
            stops,
            distanceKm = 150,
            journeyMinutes = offsets[offsets.Length - 1],
            status,
            createdAt = new DateTimeOffset(2026, 10, 1, 4, 30, 0, TimeSpan.Zero),
            deactivatedAt = (DateTimeOffset?)null,
        };
    }

    // Fleet's Bus shape, with the parts these pages read (a list answer carries the same names).
    private static object Bus(Guid busId, string name, string status, string[] seatClasses)
    {
        return new
        {
            busId,
            operatorId = OperatorId,
            registrationNumber = "MH12AB1234",
            name,
            busType = "sleeper",
            isAc = true,
            amenities = Array.Empty<string>(),
            seatClasses,
            totalSeats = 36,
            status,
            coverPhotoUrl = (string?)null,
        };
    }
}
