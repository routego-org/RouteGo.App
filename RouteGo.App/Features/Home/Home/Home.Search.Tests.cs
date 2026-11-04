using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Home.Home;

public sealed class HomeSearchTests
{
    private const string FormSelector = "[data-search-form]";

    [Fact]
    public async Task Get_Visitor_ShowsSearchFormWithTodayWithoutCallingSearch()
    {
        // Arrange
        using var host = new FleetTestHost();
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));

        // Act
        IDocument page = await visitor.GetPageAsync("/");

        // Assert
        Assert.NotNull(page.QuerySelector("[data-visitor-home]"));
        Assert.Equal("Find a bus", page.QuerySelector("[data-search-title]")!.TextContent);
        Assert.Equal(new[] { "From city", "To city", "Travel date" }, Labels(page));
        Assert.Equal("2026-10-02", FieldValue(page, "Search.Date"));
        Assert.Equal("Search", page.QuerySelector("[data-search]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-search-filters]"));
        Assert.Empty(host.Search.Requests);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Operator")]
    [InlineData("Admin")]
    public async Task Get_SignedIn_ShowsSearchFormWithWelcome(string role)
    {
        // Arrange
        using var host = new FleetTestHost();
        FormClient browser = await host.SignInAsync(role);

        // Act
        IDocument page = await browser.GetPageAsync("/");

        // Assert
        Assert.NotNull(page.QuerySelector("[data-welcome]"));
        Assert.Equal(new[] { "From city", "To city", "Travel date" }, Labels(page));
        Assert.Equal("2026-10-02", FieldValue(page, "Search.Date"));
        Assert.Empty(host.Search.Requests);
    }

    [Fact]
    public async Task Post_PickedCitiesAndDate_GoesToResultsAddress()
    {
        // Arrange
        using var host = new FleetTestHost();
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));
        IDocument page = await visitor.GetPageAsync("/");

        // Act
        HttpResponseMessage response = await visitor.SendFormAsync(page, FormSelector, MumbaiToPune());

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SearchStubs.ResultsAddress, response.Headers.Location!.OriginalString);
        Assert.Empty(host.Search.Requests);
    }

    [Fact]
    public async Task Post_FromEmpty_ShowsRequiredAndStaysHome()
    {
        // Arrange
        using var host = new FleetTestHost();
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));
        IDocument page = await visitor.GetPageAsync("/");
        Dictionary<string, string> fields = MumbaiToPune();
        fields["Search.FromCity"] = "";
        fields["Search.FromCityId"] = "";
        fields["Search.FromCityName"] = "";

        // Act
        HttpResponseMessage response = await visitor.SendFormAsync(page, FormSelector, fields);
        IDocument again = await visitor.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("From city is required.", CityError(again, "from"));
        Assert.NotNull(again.QuerySelector("[data-visitor-home]"));
        Assert.Empty(host.Search.Requests);
    }

    [Fact]
    public async Task Post_TextTypedOverPickedCity_ShowsChooseFromList()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.CitiesPath, () => FakeBackend.Json(SearchStubs.Cities()));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));
        IDocument page = await visitor.GetPageAsync("/");
        Dictionary<string, string> fields = MumbaiToPune();
        fields["Search.FromCity"] = "Mumbia";

        // Act
        HttpResponseMessage response = await visitor.SendFormAsync(page, FormSelector, fields);
        IDocument again = await visitor.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("Choose a city from the list.", CityError(again, "from"));
        Assert.Equal("No matching cities.", again.QuerySelector("[data-city-field='from'] [data-city-note]")!.TextContent);
        Assert.Equal("Mumbia", FieldValue(again, "Search.FromCity"));
        Assert.Equal(0, SearchStubs.Calls(host.Search, SearchStubs.TripsPath));
    }

    [Fact]
    public async Task Post_ToTypedNotPicked_AsksSuggestionsFollowingFrom()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.CitiesPath, () => FakeBackend.Json(SearchStubs.Cities(new SuggestedCity(SearchStubs.Pune, "Pune"))));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));
        IDocument page = await visitor.GetPageAsync("/");
        Dictionary<string, string> fields = MumbaiToPune();
        fields["Search.ToCity"] = "P";
        fields["Search.ToCityId"] = "";

        // Act
        HttpResponseMessage response = await visitor.SendFormAsync(page, FormSelector, fields);
        IDocument again = await visitor.ReadDocumentAsync(response);

        // Assert
        Dictionary<string, string> query = SearchStubs.LastQuery(host.Search, SearchStubs.CitiesPath);
        Assert.Equal("to", query["cityRole"]);
        Assert.Equal(SearchStubs.Mumbai.ToString(), query["fromCityId"]);
        Assert.Equal($"{SearchStubs.Pune}|Pune", again.QuerySelector("[data-city-field='to'] [data-city-choice]")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Post_SameCityTwice_ShowsDifferentCityMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));
        IDocument page = await visitor.GetPageAsync("/");
        Dictionary<string, string> fields = MumbaiToPune();
        fields["Search.ToCity"] = "Mumbai";
        fields["Search.ToCityId"] = SearchStubs.Mumbai.ToString();
        fields["Search.ToCityName"] = "Mumbai";

        // Act
        HttpResponseMessage response = await visitor.SendFormAsync(page, FormSelector, fields);
        IDocument again = await visitor.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("Choose a To city different from the From city.", CityError(again, "to"));
        Assert.Empty(host.Search.Requests);
    }

    [Fact]
    public async Task Post_YesterdayAsTravelDate_ShowsTodayOrLater()
    {
        // Arrange
        using var host = new FleetTestHost();
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));
        IDocument page = await visitor.GetPageAsync("/");
        Dictionary<string, string> fields = MumbaiToPune();
        fields["Search.Date"] = "2026-10-01";

        // Act
        HttpResponseMessage response = await visitor.SendFormAsync(page, FormSelector, fields);
        IDocument again = await visitor.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Choose today or a later date.", again.QuerySelector("[data-date-error]")!.TextContent);
        Assert.Empty(host.Search.Requests);
    }

    private static Dictionary<string, string> MumbaiToPune()
    {
        return new Dictionary<string, string>
        {
            ["Search.FromCity"] = "Mumbai",
            ["Search.FromCityId"] = SearchStubs.Mumbai.ToString(),
            ["Search.FromCityName"] = "Mumbai",
            ["Search.ToCity"] = "Pune",
            ["Search.ToCityId"] = SearchStubs.Pune.ToString(),
            ["Search.ToCityName"] = "Pune",
            ["Search.Date"] = SearchStubs.TravelDate,
        };
    }

    private static string[] Labels(IDocument page)
    {
        return page.QuerySelectorAll("[data-search-form] .form-label").Select(label => label.TextContent).ToArray();
    }

    private static string FieldValue(IDocument page, string name)
    {
        return page.QuerySelector($"[name='{name}']")!.GetAttribute("value") ?? "";
    }

    private static string CityError(IDocument page, string role)
    {
        return page.QuerySelector($"[data-city-field='{role}'] [data-city-error]")!.TextContent;
    }
}
