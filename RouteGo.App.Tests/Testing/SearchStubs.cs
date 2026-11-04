using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Tests.Testing;

/// <summary>
/// The search the bus search tests share: Mumbai to Pune on 25 Oct 2026, where Search knows T-202
/// (Sharma Express, 7:00 AM), T-201 (BusCo Volvo 9600, 10:00 PM, via Lonavala, arriving the next
/// day) and T-203 (Konkan Star, 2:00 PM, sold out) — and the addresses of Search's operations.
/// </summary>
public static class SearchStubs
{
    public static readonly Guid Mumbai = Guid.Parse("11111111-0000-4000-8000-000000000001");
    public static readonly Guid Pune = Guid.Parse("11111111-0000-4000-8000-000000000002");
    public static readonly Guid Lonavala = Guid.Parse("11111111-0000-4000-8000-000000000003");
    public static readonly Guid Nashik = Guid.Parse("11111111-0000-4000-8000-000000000004");

    public static readonly Guid T201 = Guid.Parse("22222222-0000-4000-8000-000000000201");
    public static readonly Guid T202 = Guid.Parse("22222222-0000-4000-8000-000000000202");
    public static readonly Guid T203 = Guid.Parse("22222222-0000-4000-8000-000000000203");
    public static readonly Guid B11 = Guid.Parse("33333333-0000-4000-8000-000000000011");
    public static readonly Guid B12 = Guid.Parse("33333333-0000-4000-8000-000000000012");
    public static readonly Guid B13 = Guid.Parse("33333333-0000-4000-8000-000000000013");

    public const string TripsPath = "/search/trips";
    public const string CitiesPath = "/search/cities";
    public const string TravelDate = "2026-10-25";

    /// <summary>The results address of Mumbai to Pune on 25 Oct 2026, with no filter.</summary>
    public static readonly string ResultsAddress =
        $"/bus-search?fromCityId={Mumbai}&fromCityName=Mumbai&toCityId={Pune}&toCityName=Pune&date={TravelDate}";

    /// <summary>A page of Search's trip search answer.</summary>
    public static object Page(object[] items, int totalCount, int page)
    {
        return new { items, totalCount, page, pageSize = 20 };
    }

    /// <summary>The three trips of the route and date, in Search's order: T-202, T-201, then the sold-out T-203.</summary>
    public static object ThreeTrips()
    {
        return Page(new[] { T202Listing(), T201Listing(), T203Listing() }, 3, 1);
    }

    /// <summary>One page of <paramref name="count"/> plain trips, for paging.</summary>
    public static object ManyTrips(int count, int totalCount, int page)
    {
        var items = new List<object>();
        for (int index = 0; index < count; index++)
        {
            items.Add(T202Listing());
        }

        return Page(items.ToArray(), totalCount, page);
    }

    public static object NoTrips(int totalCount)
    {
        return Page(Array.Empty<object>(), totalCount, 1);
    }

    /// <summary>Search's city suggestions answer.</summary>
    public static object Cities(params SuggestedCity[] cities)
    {
        var items = new List<object>();
        foreach (SuggestedCity city in cities)
        {
            items.Add(new { cityId = city.CityId, name = city.Name });
        }

        return new { items };
    }

    /// <summary>The query of the last request Search got at the path, one value per name.</summary>
    public static Dictionary<string, string> LastQuery(FakeBackend search, string path)
    {
        HttpRequestMessage request = search.Requests.Last(sent => sent.RequestUri!.AbsolutePath == path);
        var values = new Dictionary<string, string>();
        foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> pair in QueryHelpers.ParseQuery(request.RequestUri!.Query))
        {
            values[pair.Key] = pair.Value.ToString();
        }

        return values;
    }

    public static int Calls(FakeBackend search, string path)
    {
        return search.Requests.Count(sent => sent.RequestUri!.AbsolutePath == path);
    }

    public static object T201Listing()
    {
        return new
        {
            tripId = T201,
            operatorName = "BusCo Travels",
            operatorRating = 4.2m,
            busId = B11,
            busName = "BusCo Volvo 9600",
            busType = "sleeper",
            isAc = true,
            amenities = new[] { "wifi", "chargingPoint" },
            fromCity = new { cityId = Mumbai, name = "Mumbai" },
            toCity = new { cityId = Pune, name = "Pune" },
            serviceDate = TravelDate,
            departureAt = "2026-10-25T16:30:00Z",
            arrivalAt = "2026-10-25T19:30:00Z",
            durationMinutes = 180,
            stops = new[] { new { cityId = Lonavala, cityName = "Lonavala", arrivalAt = "2026-10-25T18:00:00Z" } },
            lowestFreeFare = 900,
            seatsLeft = 36,
            isSoldOut = false,
        };
    }

    public static object T202Listing()
    {
        return new
        {
            tripId = T202,
            operatorName = "Sharma Travels",
            operatorRating = 3.8m,
            busId = B12,
            busName = "Sharma Express",
            busType = "seater",
            isAc = false,
            amenities = Array.Empty<string>(),
            fromCity = new { cityId = Mumbai, name = "Mumbai" },
            toCity = new { cityId = Pune, name = "Pune" },
            serviceDate = TravelDate,
            departureAt = "2026-10-25T01:30:00Z",
            arrivalAt = "2026-10-25T04:30:00Z",
            durationMinutes = 180,
            stops = Array.Empty<object>(),
            lowestFreeFare = 500,
            seatsLeft = 12,
            isSoldOut = false,
        };
    }

    public static object T203Listing()
    {
        return new
        {
            tripId = T203,
            operatorName = "Konkan Lines",
            operatorRating = 4.6m,
            busId = B13,
            busName = "Konkan Star",
            busType = "semiSleeper",
            isAc = true,
            amenities = Array.Empty<string>(),
            fromCity = new { cityId = Mumbai, name = "Mumbai" },
            toCity = new { cityId = Pune, name = "Pune" },
            serviceDate = TravelDate,
            departureAt = "2026-10-25T08:30:00Z",
            arrivalAt = "2026-10-25T11:30:00Z",
            durationMinutes = 180,
            stops = Array.Empty<object>(),
            lowestFreeFare = 450,
            seatsLeft = 0,
            isSoldOut = true,
        };
    }
}
