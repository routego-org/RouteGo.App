using System.Globalization;
using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.NewRoute;

public sealed class NewRouteChecksTests
{
    private const string PageUrl = "https://localhost/operator/routes/new";
    private const string TimeInvalid = "Enter hours (0 to 48) and minutes (0 to 59).";
    private const string DistanceInvalid = "Distance (km) must be between 1 and 3,000.";

    [Theory]
    [InlineData("Input.Stops[1].CityId=", 1, "[data-city-error]", "City is required.")]
    [InlineData("Input.Stops[1].CityId=" + NewRouteTestData.PuneId, 2, "[data-city-error]", "Pune is already a stop on this route.")]
    [InlineData("Input.Stops[2].Hours=1;Input.Stops[2].Minutes=50", 2, "[data-time-error]", "This stop must come later than the stop before it.")]
    [InlineData("Input.Stops[1].Hours=0;Input.Stops[1].Minutes=0", 1, "[data-time-error]", "This stop must come later than the stop before it.")]
    [InlineData("Input.Stops[2].Hours=48;Input.Stops[2].Minutes=1", 2, "[data-time-error]", "A journey can take at most 48 hours.")]
    [InlineData("Input.Stops[1].Minutes=60", 1, "[data-time-error]", TimeInvalid)]
    [InlineData("Input.Stops[1].Hours=1.5", 1, "[data-time-error]", TimeInvalid)]
    [InlineData("Input.Stops[2].Hours=49", 2, "[data-time-error]", TimeInvalid)]
    [InlineData("Input.Stops[1].Minutes=", 1, "[data-time-error]", TimeInvalid)]
    [InlineData("Input.DistanceKm=0", -1, "[data-distance-error]", DistanceInvalid)]
    [InlineData("Input.DistanceKm=3001", -1, "[data-distance-error]", DistanceInvalid)]
    [InlineData("Input.DistanceKm=150.5", -1, "[data-distance-error]", DistanceInvalid)]
    [InlineData("Input.DistanceKm=", -1, "[data-distance-error]", DistanceInvalid)]
    public async Task Post_PageCheckFails_ShowsMessageAtItsPlaceAndSendsNothing(string changes, int stop, string place, string expected)
    {
        // Arrange
        using FleetTestHost host = NewRouteTestData.Host();
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(NewRouteTestData.PageAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, NewRouteTestData.Form, FieldsWith(changes));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, NewRouteTestData.MessageAt(result, stop, place));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Post, NewRouteTestData.RoutesPath));
    }

    [Fact]
    public async Task Post_SeveralProblems_ShowsEachAtItsPlaceAndKeepsWhatWasTyped()
    {
        // Arrange
        using FleetTestHost host = NewRouteTestData.Host();
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(NewRouteTestData.PageAddress);
        Dictionary<string, string> fields = FieldsWith("Input.Stops[0].CityId=;Input.Stops[1].Hours=1.5;Input.DistanceKm=150.5");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, NewRouteTestData.Form, fields);
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("City is required.", NewRouteTestData.MessageAt(result, 0, "[data-city-error]"));
        Assert.Equal(TimeInvalid, NewRouteTestData.MessageAt(result, 1, "[data-time-error]"));
        Assert.Equal("", NewRouteTestData.MessageAt(result, 2, "[data-time-error]"));
        Assert.Equal(DistanceInvalid, NewRouteTestData.MessageAt(result, -1, "[data-distance-error]"));
        Assert.Equal(new[] { "", NewRouteTestData.LonavalaId, NewRouteTestData.PuneId }, NewRouteTestData.Values(result, "[data-city] option[selected]"));
        Assert.Equal(new[] { "1.5", "3" }, NewRouteTestData.Values(result, "[data-hours]"));
        Assert.Equal(new[] { "50", "30" }, NewRouteTestData.Values(result, "[data-minutes]"));
        Assert.Equal("150.5", result.QuerySelector("[data-distance]")!.GetAttribute("value"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Post, NewRouteTestData.RoutesPath));
    }

    [Fact]
    public async Task Post_AddStop_AddsEmptyStopBeforeLastAndKeepsWhatWasTyped()
    {
        // Arrange
        using FleetTestHost host = NewRouteTestData.Host();
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(NewRouteTestData.PageAddress);
        Dictionary<string, string> fields = FieldsWith("Input.Stops[1].CityId=" + NewRouteTestData.PuneId + ";Input.Stops[1].Hours=3;Input.Stops[1].Minutes=30");
        fields.Remove("Input.Stops[2].CityId");
        fields.Remove("Input.Stops[2].Hours");
        fields.Remove("Input.Stops[2].Minutes");

        // Act
        HttpResponseMessage response = await form.SendButtonAsync(page, "[data-add-stop]", fields);
        IDocument result = await form.ReadDocumentAsync(response, PageUrl);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, result.QuerySelectorAll("[data-stop]").Length);
        Assert.Equal(new[] { "First stop", "Last stop" }, NewRouteTestData.Texts(result, "[data-stop] legend"));
        Assert.Equal(new[] { NewRouteTestData.MumbaiId, NewRouteTestData.PuneId }, NewRouteTestData.Values(result, "[data-city] option[selected]"));
        Assert.Equal(new[] { "", "3" }, NewRouteTestData.Values(result, "[data-hours]"));
        Assert.Equal("150", result.QuerySelector("[data-distance]")!.GetAttribute("value"));
        Assert.Single(result.QuerySelectorAll("[data-remove-stop]"));
        Assert.NotNull(result.QuerySelectorAll("[data-stop]")[1].QuerySelector("[data-remove-stop]"));
        Assert.Empty(result.QuerySelectorAll("[data-error], .field-validation-error"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Post, NewRouteTestData.RoutesPath));
    }

    [Fact]
    public async Task Post_AddStopToElevenStops_ReachesTwelveAndOffersNoMore()
    {
        // Arrange
        using FleetTestHost host = NewRouteTestData.Host();
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(NewRouteTestData.PageAddress);

        // Act
        HttpResponseMessage response = await form.SendButtonAsync(page, "[data-add-stop]", Stops(11));
        IDocument result = await form.ReadDocumentAsync(response, PageUrl);

        // Assert
        Assert.Equal(12, result.QuerySelectorAll("[data-stop]").Length);
        Assert.Null(result.QuerySelector("[data-add-stop]"));
        Assert.Equal("A route can have at most 12 stops.", result.QuerySelector("[data-most-stops]")!.TextContent);
        Assert.Equal(10, result.QuerySelectorAll("[data-remove-stop]").Length);
        Assert.Null(result.QuerySelectorAll("[data-stop]")[0].QuerySelector("[data-remove-stop]"));
        Assert.Null(result.QuerySelectorAll("[data-stop]")[11].QuerySelector("[data-remove-stop]"));
    }

    [Fact]
    public async Task Post_RemoveStop_RemovesOnlyThatStopAndKeepsTheRest()
    {
        // Arrange
        using FleetTestHost host = NewRouteTestData.Host();
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(NewRouteTestData.PageAddress);
        HttpResponseMessage added = await form.SendButtonAsync(page, "[data-add-stop]", NewRouteTestData.ValidFields());
        IDocument fourStops = await form.ReadDocumentAsync(added, PageUrl);
        Dictionary<string, string> fields = FieldsWith("Input.Stops[3].CityId=" + NewRouteTestData.PuneId + ";Input.Stops[3].Hours=3;Input.Stops[3].Minutes=30");
        fields["Input.Stops[2].CityId"] = NewRouteTestData.NashikId;
        fields["Input.Stops[2].Hours"] = "2";
        fields["Input.Stops[2].Minutes"] = "40";

        // Act
        HttpResponseMessage response = await form.SendButtonAsync(fourStops, "[data-stop]:nth-of-type(2) [data-remove-stop]", fields);
        IDocument result = await form.ReadDocumentAsync(response, PageUrl);

        // Assert
        Assert.Equal(3, result.QuerySelectorAll("[data-stop]").Length);
        Assert.Equal(new[] { NewRouteTestData.MumbaiId, NewRouteTestData.NashikId, NewRouteTestData.PuneId }, NewRouteTestData.Values(result, "[data-city] option[selected]"));
        Assert.Equal(new[] { "2", "3" }, NewRouteTestData.Values(result, "[data-hours]"));
        Assert.Equal(new[] { "40", "30" }, NewRouteTestData.Values(result, "[data-minutes]"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Post, NewRouteTestData.RoutesPath));
    }

    // The valid three-stop form with each "field=value" in the semicolon-separated list put in.
    private static Dictionary<string, string> FieldsWith(string changes)
    {
        Dictionary<string, string> fields = NewRouteTestData.ValidFields();
        foreach (string change in changes.Split(';'))
        {
            string[] parts = change.Split('=', 2);
            fields[parts[0]] = parts[1];
        }

        return fields;
    }

    // A form with the number of stops, each with only its hours typed.
    private static Dictionary<string, string> Stops(int count)
    {
        var fields = new Dictionary<string, string>();
        for (int index = 0; index < count; index++)
        {
            fields[$"Input.Stops[{index}].Hours"] = index.ToString(CultureInfo.InvariantCulture);
        }

        return fields;
    }
}
