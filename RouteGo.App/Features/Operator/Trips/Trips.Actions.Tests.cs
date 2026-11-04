using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.Trips;

public sealed class TripsActionsTests
{
    private const string FareForm = "form[data-fare-form]";
    private const string CancelForm = "form[data-cancel-form]";

    private static readonly string _faresPath = $"/trips/{TripsTests.OpenTripId}/fares";
    private static readonly string _cancelPath = $"/trips/{TripsTests.FrozenTripId}/cancel";

    [Fact]
    public async Task GetFares_OpenTrip_OpensFormWithCurrentFares()
    {
        // Arrange
        using var host = new FleetTestHost();
        TripsTests.MapFleet(host, TripsTests.FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument list = await form.GetPageAsync(TripsTests.PageAddress + "?status=scheduled");
        string faresAddress = list.QuerySelector($"[data-trip='{TripsTests.OpenTripId}'] [data-change-fares]")!.GetAttribute("href")!;

        // Act
        IDocument page = await form.GetPageAsync(faresAddress);

        // Assert
        Assert.Equal($"/operator/trips?status=scheduled&pageNumber=1&fares={TripsTests.OpenTripId}", faresAddress);
        Assert.Equal("Change fares — Mumbai → Pune on 3 Oct 2026 at 10:30 PM IST", page.QuerySelector("[data-fare-form-title]")!.TextContent);
        Assert.Equal(new[] { "Lower berth (₹)", "Upper berth (₹)" }, TripsTests.Texts(page, "[data-fare-label]"));
        Assert.Equal(new[] { "1200", "1000" }, page.QuerySelectorAll("[data-fare-box]").Select(box => box.GetAttribute("value")!).ToArray());
        Assert.Equal("Save fares", page.QuerySelector("[data-save-fares]")!.TextContent);
        Assert.Equal("Cancel", page.QuerySelector("[data-fare-cancel]")!.TextContent);
    }

    [Fact]
    public async Task PostFares_FaresChanged_SendsThemAndShowsListWithConfirmation()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Put, _faresPath, () => FakeBackend.Json(TripsTests.Trip(TripsTests.OpenTripId, "scheduled", "2026-10-03T17:00:00Z")));
        TripsTests.MapFleet(host, TripsTests.FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync($"{TripsTests.PageAddress}?status=scheduled&fares={TripsTests.OpenTripId}");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, FareForm, Fares("1500", "1250"));
        string? sentBody = host.Fleet.LastRequestBody;
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/operator/trips?status=scheduled&pageNumber=1", response.Headers.Location!.OriginalString);
        Assert.Equal("{\"classFares\":[{\"seatClass\":\"Lower berth\",\"fare\":1500},{\"seatClass\":\"Upper berth\",\"fare\":1250}]}", sentBody);
        Assert.Equal("Fares changed. New bookings pay the new fares.", result.QuerySelector("[data-flash]")!.TextContent);
    }

    [Fact]
    public async Task PostFares_SameAsBefore_SendsNothingAndSaysSo()
    {
        // Arrange
        using var host = new FleetTestHost();
        TripsTests.MapFleet(host, TripsTests.FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync($"{TripsTests.PageAddress}?fares={TripsTests.OpenTripId}");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, FareForm, Fares("1200", "1000"));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("The fares are the same as before. Nothing was changed.", result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal(0, host.FleetCalls(HttpMethod.Put, _faresPath));
        Assert.Null(result.QuerySelector(FareForm));
    }

    [Fact]
    public async Task PostFares_FareNotAllowed_ShowsFareMessageKeepsTyped()
    {
        // Arrange
        using var host = new FleetTestHost();
        TripsTests.MapFleet(host, TripsTests.FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync($"{TripsTests.PageAddress}?fares={TripsTests.OpenTripId}");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, FareForm, Fares("1500", "49"));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(new[] { "", Messages.ValidationFare }, TripsTests.Texts(result, "[data-fare-error]"));
        Assert.Equal("49", result.QuerySelectorAll("[data-fare-box]")[1].GetAttribute("value"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Put, _faresPath));
    }

    [Fact]
    public async Task PostFares_CutoffPassed_ShowsWhenFaresFrozeAndListAgain()
    {
        // Arrange
        using var host = new FleetTestHost();
        var cutoff = new Dictionary<string, object> { ["cutoffAt"] = "2026-10-03T11:00:00Z" };
        host.Fleet.Map(HttpMethod.Put, _faresPath, () => IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "FARE_CHANGE_CUTOFF_PASSED", cutoff));
        TripsTests.MapFleet(host, TripsTests.FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync($"{TripsTests.PageAddress}?fares={TripsTests.OpenTripId}");
        int listsBefore = host.FleetCalls(HttpMethod.Get, TripsTests.TripsPath);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, FareForm, Fares("1500", "1250"));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("Fares are frozen 6 hours before departure. This trip's fares froze at 3 Oct 2026, 4:30 PM IST.", result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal(listsBefore + 2, host.FleetCalls(HttpMethod.Get, TripsTests.TripsPath));
        Assert.Null(result.QuerySelector(FareForm));
        Assert.NotNull(result.QuerySelector("[data-trips]"));
    }

    [Fact]
    public async Task PostFares_FleetUnreachable_KeepsFormWithTypedFares()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Put, _faresPath, () => throw new HttpRequestException("connection refused"));
        TripsTests.MapFleet(host, TripsTests.FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync($"{TripsTests.PageAddress}?fares={TripsTests.OpenTripId}");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, FareForm, Fares("1500", "1250"));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("1500", result.QuerySelectorAll("[data-fare-box]")[0].GetAttribute("value"));
    }

    [Fact]
    public async Task GetCancel_ScheduledTrip_AsksWithTripAndReasonAndSendsNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        TripsTests.MapFleet(host, TripsTests.FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync($"{TripsTests.PageAddress}?cancel={TripsTests.FrozenTripId}");

        // Assert
        Assert.Equal(
            "Cancel the trip Mumbai → Pune on 2 Oct 2026 at 8:30 PM IST? Customers who booked it will get a refund. You cannot undo this.",
            page.QuerySelector("[data-confirm-question]")!.TextContent);
        Assert.Equal("Reason", page.QuerySelector($"{CancelForm} label")!.TextContent);
        Assert.Equal("Cancel trip", page.QuerySelector("[data-confirm-cancel]")!.TextContent);
        Assert.Equal("Go back", page.QuerySelector("[data-go-back]")!.TextContent);
        Assert.Equal(0, host.FleetCalls(HttpMethod.Post, _cancelPath));
    }

    [Fact]
    public async Task PostCancel_ReasonGiven_CancelsAndShowsConfirmation()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, _cancelPath, () => FakeBackend.Json(TripsTests.Trip(TripsTests.FrozenTripId, "cancelled", "2026-10-02T15:00:00Z")));
        TripsTests.MapFleet(host, TripsTests.FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync($"{TripsTests.PageAddress}?cancel={TripsTests.FrozenTripId}");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, CancelForm, Reason("Bus breakdown at depot"));
        string? sentBody = host.Fleet.LastRequestBody;
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("{\"reason\":\"Bus breakdown at depot\"}", sentBody);
        Assert.Equal("Trip cancelled.", result.QuerySelector("[data-flash]")!.TextContent);
    }

    [Fact]
    public async Task PostCancel_ReasonTooShort_ShowsLengthMessageKeepsQuestion()
    {
        // Arrange
        using var host = new FleetTestHost();
        TripsTests.MapFleet(host, TripsTests.FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync($"{TripsTests.PageAddress}?cancel={TripsTests.FrozenTripId}");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, CancelForm, Reason("broken"));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("Reason must be between 10 and 500 characters.", result.QuerySelector("[data-reason-error]")!.TextContent);
        Assert.Equal("broken", result.QuerySelector("[data-reason]")!.TextContent);
        Assert.Equal(0, host.FleetCalls(HttpMethod.Post, _cancelPath));
    }

    [Fact]
    public async Task PostCancel_FleetRefusesReason_ShowsLengthMessageKeepsQuestion()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Post, _cancelPath, () => IdentityStubs.ValidationProblem("reason", "The reason is too short."));
        TripsTests.MapFleet(host, TripsTests.FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync($"{TripsTests.PageAddress}?cancel={TripsTests.FrozenTripId}");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, CancelForm, Reason("Bus breakdown at depot"));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(Messages.ValidationReasonLength, result.QuerySelector("[data-reason-error]")!.TextContent);
        Assert.Equal("Bus breakdown at depot", result.QuerySelector("[data-reason]")!.TextContent);
    }

    [Theory]
    [InlineData("TRIP_DEPARTED", "This trip has already left. Nothing was changed.")]
    [InlineData("TRIP_NOT_SCHEDULED", "This trip is cancelled. Nothing was changed.")]
    public async Task PostCancel_TripMovedOn_ShowsWhyAndListAgain(string code, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        var status = new Dictionary<string, object> { ["currentStatus"] = "cancelled" };
        host.Fleet.Map(HttpMethod.Post, _cancelPath, () => IdentityStubs.Problem(HttpStatusCode.Conflict, code, status));
        TripsTests.MapFleet(host, TripsTests.FourTrips());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync($"{TripsTests.PageAddress}?cancel={TripsTests.FrozenTripId}");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(page, CancelForm, Reason("Bus breakdown at depot"));
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(expected, result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(result.QuerySelector("[data-confirm]"));
        Assert.NotNull(result.QuerySelector("[data-trips]"));
    }

    private static Dictionary<string, string> Fares(string lowerBerth, string upperBerth)
    {
        return new Dictionary<string, string>
        {
            ["FaresInput.TripId"] = TripsTests.OpenTripId.ToString(),
            ["FaresInput.Fares[0].SeatClass"] = "Lower berth",
            ["FaresInput.Fares[0].Fare"] = lowerBerth,
            ["FaresInput.Fares[1].SeatClass"] = "Upper berth",
            ["FaresInput.Fares[1].Fare"] = upperBerth,
        };
    }

    private static Dictionary<string, string> Reason(string reason)
    {
        return new Dictionary<string, string>
        {
            ["CancelInput.TripId"] = TripsTests.FrozenTripId.ToString(),
            ["CancelInput.Reason"] = reason,
        };
    }
}
