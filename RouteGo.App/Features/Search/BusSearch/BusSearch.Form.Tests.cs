using System.Net;
using System.Text.Json;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Search.BusSearch;

public sealed class BusSearchFormTests
{
    private const string FormSelector = "[data-search-form]";

    [Fact]
    public async Task Post_PriceFromAbovePriceUpTo_ShowsMessageWithoutSearching()
    {
        // Arrange
        using var host = new FleetTestHost();
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));
        IDocument page = await visitor.GetPageAsync("/bus-search");
        Dictionary<string, string> fields = MumbaiToPune(SearchStubs.TravelDate);
        fields["Search.MinFare"] = "1000";
        fields["Search.MaxFare"] = "800";

        // Act
        HttpResponseMessage response = await visitor.SendFormAsync(page, FormSelector, fields);
        IDocument again = await visitor.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Price from must not be more than Price up to.", Text(again.DocumentElement, "[data-min-fare-error]"));
        Assert.Equal("1000", FieldValue(again, "Search.MinFare"));
        Assert.Empty(host.Search.Requests);
    }

    [Fact]
    public async Task Post_PriceNotWholeRupees_ShowsFormatMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));
        IDocument page = await visitor.GetPageAsync("/bus-search");
        Dictionary<string, string> fields = MumbaiToPune(SearchStubs.TravelDate);
        fields["Search.MaxFare"] = "8.50";

        // Act
        HttpResponseMessage response = await visitor.SendFormAsync(page, FormSelector, fields);
        IDocument again = await visitor.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("Enter a valid price up to.", Text(again.DocumentElement, "[data-max-fare-error]"));
        Assert.Empty(host.Search.Requests);
    }

    [Fact]
    public async Task Post_CityTypedNotPicked_OffersMatchingCitiesToChoose()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.CitiesPath, () => FakeBackend.Json(SearchStubs.Cities(new SuggestedCity(SearchStubs.Mumbai, "Mumbai"))));
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));
        IDocument page = await visitor.GetPageAsync("/bus-search");
        Dictionary<string, string> fields = MumbaiToPune(SearchStubs.TravelDate);
        fields["Search.FromCity"] = "Mum";

        // Act
        HttpResponseMessage response = await visitor.SendFormAsync(page, FormSelector, fields);
        IDocument again = await visitor.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("Choose a city from the list.", Text(again.QuerySelector("[data-city-field='from']")!, "[data-city-error]"));
        IElement choice = again.QuerySelector("[data-city-field='from'] [data-city-choice]")!;
        Assert.Equal($"{SearchStubs.Mumbai}|Mumbai", choice.GetAttribute("value"));
        Dictionary<string, string> query = SearchStubs.LastQuery(host.Search, SearchStubs.CitiesPath);
        Assert.Equal("Mum", query["prefix"]);
        Assert.Equal("from", query["cityRole"]);
        Assert.Equal(0, SearchStubs.Calls(host.Search, SearchStubs.TripsPath));
    }

    [Fact]
    public async Task Post_CityChosenFromList_GoesToResults()
    {
        // Arrange
        using var host = new FleetTestHost();
        var visitor = new FormClient(TestHost.CreateClient(host.Factory));
        IDocument page = await visitor.GetPageAsync("/bus-search");
        Dictionary<string, string> fields = MumbaiToPune(SearchStubs.TravelDate);
        fields["Search.FromCity"] = "Mum";
        fields["Search.FromCityId"] = "";
        fields["Search.FromPick"] = $"{SearchStubs.Mumbai}|Mumbai";

        // Act
        HttpResponseMessage response = await visitor.SendFormAsync(page, FormSelector, fields);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SearchStubs.ResultsAddress, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task GetCities_FromPrefix_AsksSearchWithoutTokenAndAnswersNames()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.CitiesPath, () => FakeBackend.Json(SearchStubs.Cities(new SuggestedCity(SearchStubs.Mumbai, "Mumbai"))));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        JsonElement answer = await CitiesAsync(visitor, "&prefix=Mu&cityRole=from");

        // Assert
        Dictionary<string, string> query = SearchStubs.LastQuery(host.Search, SearchStubs.CitiesPath);
        Assert.Equal("Mu", query["prefix"]);
        Assert.Equal("from", query["cityRole"]);
        Assert.False(query.ContainsKey("fromCityId"));
        Assert.Null(host.Search.LastRequestHeader("Authorization"));
        Assert.Equal("Mumbai", answer.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal(SearchStubs.Mumbai, answer.GetProperty("items")[0].GetProperty("cityId").GetGuid());
    }

    [Fact]
    public async Task GetCities_ToPrefixAfterFrom_SendsFromCity()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.CitiesPath, () => FakeBackend.Json(SearchStubs.Cities(new SuggestedCity(SearchStubs.Pune, "Pune"))));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        JsonElement answer = await CitiesAsync(visitor, $"&prefix=P&cityRole=to&fromCityId={SearchStubs.Mumbai}");

        // Assert
        Dictionary<string, string> query = SearchStubs.LastQuery(host.Search, SearchStubs.CitiesPath);
        Assert.Equal("to", query["cityRole"]);
        Assert.Equal(SearchStubs.Mumbai.ToString(), query["fromCityId"]);
        Assert.Equal("Pune", answer.GetProperty("items")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetCities_NoMatch_AnswersNoMatchingCities()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.CitiesPath, () => FakeBackend.Json(SearchStubs.Cities()));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        JsonElement answer = await CitiesAsync(visitor, "&prefix=Xy&cityRole=from");

        // Assert
        Assert.Equal(0, answer.GetProperty("items").GetArrayLength());
        Assert.Equal("No matching cities.", answer.GetProperty("note").GetString());
    }

    [Fact]
    public async Task GetCities_SearchUnreachable_AnswersSuggestionsUnavailable()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Search.Map(HttpMethod.Get, SearchStubs.CitiesPath, () => throw new HttpRequestException("connection refused"));
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        JsonElement answer = await CitiesAsync(visitor, "&prefix=Mu&cityRole=from");

        // Assert
        Assert.Equal("We can't suggest cities right now. Please try again in a moment.", answer.GetProperty("note").GetString());
    }

    [Fact]
    public async Task GetCities_OnlySpaces_DoesNotAskSearch()
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient visitor = TestHost.CreateClient(host.Factory);

        // Act
        JsonElement answer = await CitiesAsync(visitor, "&prefix=%20%20&cityRole=from");

        // Assert
        Assert.Empty(host.Search.Requests);
        Assert.Equal(0, answer.GetProperty("items").GetArrayLength());
    }

    private static async Task<JsonElement> CitiesAsync(HttpClient client, string query)
    {
        HttpResponseMessage response = await client.GetAsync(SearchForm.CitiesAddress + query);
        string body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static Dictionary<string, string> MumbaiToPune(string date)
    {
        return new Dictionary<string, string>
        {
            ["Search.FromCity"] = "Mumbai",
            ["Search.FromCityId"] = SearchStubs.Mumbai.ToString(),
            ["Search.FromCityName"] = "Mumbai",
            ["Search.ToCity"] = "Pune",
            ["Search.ToCityId"] = SearchStubs.Pune.ToString(),
            ["Search.ToCityName"] = "Pune",
            ["Search.Date"] = date,
        };
    }

    private static string FieldValue(IDocument page, string name)
    {
        return page.QuerySelector($"[name='{name}']")!.GetAttribute("value") ?? "";
    }

    private static string Text(IElement scope, string selector)
    {
        return scope.QuerySelector(selector)!.TextContent;
    }
}
