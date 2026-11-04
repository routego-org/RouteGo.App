using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Buses.PublicBus;

public sealed class PublicBusTests
{
    private const string PhotoP3 = "https://media.routego.example/buses/shivneri/p3.jpg";
    private const string PhotoP1 = "https://media.routego.example/buses/shivneri/p1.jpg";

    private static readonly Guid _busId = Guid.Parse("0b9c8d7e-6f5a-4b3c-9d2e-1f0a9b8c7d6e");
    private static readonly string _pageAddress = $"/buses/{_busId}";
    private static readonly string _fleetPath = $"/public/buses/{_busId}";

    [Fact]
    public async Task Get_Visitor_ShowsBusWithoutSignInOrToken()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FakeBackend.Json(Shivneri(PhotosP3ThenP1())));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await visitor.GetAsync(_pageAddress);
        IDocument page = await new FormClient(visitor).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Shivneri Sleeper", page.QuerySelector("[data-bus-name]")!.TextContent);
        Assert.Equal(new[] { PhotoP3, PhotoP1 }, page.QuerySelectorAll("[data-photo]").Select(photo => photo.GetAttribute("src")).ToArray());
        Assert.Equal("Sleeper", Field(page, "bus-type"));
        Assert.Equal("Yes", Field(page, "ac"));
        Assert.Equal("Blanket, Charging point", Field(page, "amenities"));
        Assert.Equal("Lower berth, Upper berth", Field(page, "seat-classes"));
        Assert.Equal("36 seats", page.QuerySelector("[data-seats]")!.TextContent);
        Assert.Equal("Bus company: BusCo Travels", page.QuerySelector("[data-company]")!.TextContent);
        Assert.Equal("Rating 4.0 out of 5", page.QuerySelector("[data-rating]")!.TextContent);
        Assert.Null(host.Fleet.LastRequestHeader("Authorization"));
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Operator")]
    [InlineData("Admin")]
    public async Task Get_SignedIn_ShowsSamePageWithoutToken(string role)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FakeBackend.Json(Shivneri(PhotosP3ThenP1())));
        FormClient browser = await host.SignInAsync(role);

        // Act
        IDocument page = await browser.GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("Shivneri Sleeper", page.QuerySelector("[data-bus-name]")!.TextContent);
        Assert.Equal("Rating 4.0 out of 5", page.QuerySelector("[data-rating]")!.TextContent);
        Assert.Null(host.Fleet.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_BusWithoutPhotosOrAmenities_SaysNoPhotosAndNone()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FakeBackend.Json(Bravo()));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        IDocument page = await new FormClient(visitor).GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal("No photos of this bus yet.", page.QuerySelector("[data-no-photos]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-photos]"));
        Assert.Equal("None", Field(page, "amenities"));
        Assert.Equal("No", Field(page, "ac"));
        Assert.Equal("Rating 3.5 out of 5", page.QuerySelector("[data-rating]")!.TextContent);
        Assert.DoesNotContain("Inactive", page.Body!.TextContent);
        Assert.DoesNotContain("Active", page.Body!.TextContent);
    }

    [Fact]
    public async Task Get_PhotoAddressNotAWebAddress_LeavesThatPhotoOut()
    {
        // Arrange
        using var host = new FleetTestHost();
        object[] photos = new object[]
        {
            new { photoId = Guid.NewGuid(), position = 1, url = "javascript:alert(1)" },
            new { photoId = Guid.NewGuid(), position = 2, url = PhotoP1 },
        };
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => FakeBackend.Json(Shivneri(photos)));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        IDocument page = await new FormClient(visitor).GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal(new[] { PhotoP1 }, page.QuerySelectorAll("[data-photo]").Select(photo => photo.GetAttribute("src")).ToArray());
    }

    [Fact]
    public async Task Get_UnknownBus_ShowsNotFoundWithGoHomeLink()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await visitor.GetAsync(_pageAddress);
        IDocument page = await new FormClient(visitor).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("We couldn't find that bus. It may have been removed.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("Go to Home", page.QuerySelector("[data-page-alert] a")!.TextContent);
        Assert.Equal("/", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector("[data-bus-fields]"));
    }

    [Fact]
    public async Task Get_FleetUnreachable_ShowsUpstreamMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, _fleetPath, () => throw new HttpRequestException("connection refused"));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        IDocument page = await new FormClient(visitor).GetPageAsync(_pageAddress);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-bus-fields]"));
    }

    private static object[] PhotosP3ThenP1()
    {
        return new object[]
        {
            new { photoId = Guid.NewGuid(), position = 1, url = PhotoP3 },
            new { photoId = Guid.NewGuid(), position = 2, url = PhotoP1 },
        };
    }

    private static object Shivneri(object[] photos)
    {
        return new
        {
            busId = _busId,
            name = "Shivneri Sleeper",
            busType = "sleeper",
            isAc = true,
            amenities = new[] { "blanket", "chargingPoint" },
            seatClasses = new[] { "Lower berth", "Upper berth" },
            totalSeats = 36,
            operatorName = "BusCo Travels",
            operatorRating = 4.0,
            photos,
        };
    }

    private static object Bravo()
    {
        return new
        {
            busId = _busId,
            name = "Bravo",
            busType = "seater",
            isAc = false,
            amenities = Array.Empty<string>(),
            seatClasses = new[] { "Seat" },
            totalSeats = 40,
            operatorName = "BusCo Travels",
            operatorRating = 3.5,
            photos = Array.Empty<object>(),
        };
    }

    private static string Field(IDocument page, string name)
    {
        return page.QuerySelector($"[data-field='{name}']")!.TextContent.Trim();
    }
}
