using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.SeatBlocking;

public sealed class SeatBlockingBlockTests
{
    [Fact]
    public async Task Block_AvailableSeats_BlocksWithoutAskingAndSaysSo()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.BlocksPath, () => FakeBackend.Json(Blocked("L1", "L2"), HttpStatusCode.Created));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.AvailableField, "L1", true);
        driver.Tick(SeatBlockingModel.AvailableField, "L2", true);

        // Act
        HttpResponseMessage response = await driver.PressAsync("[data-block]");
        IDocument page = await new FormClient(client).GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SeatStubs.OperatorPage, response.Headers.Location!.OriginalString);
        Assert.Equal("{\"seatNos\":[\"L1\",\"L2\"]}", host.SeatInventory.LastRequestBody);
        Assert.Equal("Bearer " + host.AccessToken, host.SeatInventory.Requests.Single(sent => sent.Method == HttpMethod.Post).Headers.Authorization!.ToString());
        Assert.Equal("Seats blocked: L1, L2. Customers cannot book them.", page.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal(2, host.SeatInventoryCalls(HttpMethod.Get, SeatStubs.AllocationsPath));
    }

    [Fact]
    public async Task Block_FreeSeatAndOwnBlockPicked_SendsOnlyTheFreeSeat()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.BlocksPath, () => FakeBackend.Json(Blocked("L1"), HttpStatusCode.Created));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.AvailableField, "L1", true);
        driver.Tick(SeatBlockingModel.BlockedField, "U2", true);

        // Act
        await driver.PressAsync("[data-block]");

        // Assert
        Assert.Equal("{\"seatNos\":[\"L1\"]}", host.SeatInventory.LastRequestBody);
    }

    [Fact]
    public async Task Block_NoAvailableSeatPicked_SaysSoAndSendsNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.BlockedField, "U2", true);

        // Act
        HttpResponseMessage response = await driver.PressAsync("[data-block]");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Pick at least one available seat to block.", driver.Text("[data-page-alert]"));
        Assert.Equal(new[] { "U2" }, SeatStubs.Ticked(driver.Page));
        Assert.Equal(0, host.SeatInventoryCalls(HttpMethod.Post, SeatStubs.BlocksPath));
    }

    [Fact]
    public async Task Block_MoreThanSixtySeats_SaysSoAndSendsNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        HttpClient client = await host.SignInClientAsync("Operator");
        var form = new FormClient(client);
        IDocument opened = await form.GetPageAsync(SeatStubs.OperatorPage);
        var picks = Enumerable.Range(1, 61).Select(number => new KeyValuePair<string, string>(SeatBlockingModel.AvailableField, "S" + number)).ToList();

        // Act
        HttpResponseMessage response = await form.SendFormAsync(opened, "[data-seat-form]", picks);
        IDocument page = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("You can block or unblock at most 60 seats at a time.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal(0, host.SeatInventoryCalls(HttpMethod.Post, SeatStubs.BlocksPath));
    }

    [Fact]
    public async Task Block_SeatTakenMeanwhile_NamesItLoadsSeatsAgainAndUnpicks()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.BlocksPath, () => SeatStubs.SeatsProblem(HttpStatusCode.Conflict, "SEATS_UNAVAILABLE", "unavailableSeats", "L2"));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.AvailableField, "L1", true);
        driver.Tick(SeatBlockingModel.AvailableField, "L2", true);

        // Act
        await driver.PressAsync("[data-block]");

        // Assert
        Assert.Equal("Some seats are no longer free: L2. Nothing was blocked. Pick the seats again.", driver.Text("[data-page-alert]"));
        Assert.Equal("{\"seatNos\":[\"L1\",\"L2\"]}", host.SeatInventory.LastRequestBody);
        Assert.Equal("Available", driver.Text("[data-seat='L1'] [data-status-label]"));
        Assert.Empty(SeatStubs.Ticked(driver.Page));
        Assert.Equal(2, host.SeatInventoryCalls(HttpMethod.Get, SeatStubs.AllocationsPath));
    }

    [Fact]
    public async Task Block_SeatsAlreadyBlocked_SaysNothingChanged()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.BlocksPath, () => FakeBackend.Json(new { tripId = SeatStubs.TripId, seatNos = new[] { "L1" }, blockedSeatNos = Array.Empty<string>() }));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.AvailableField, "L1", true);

        // Act
        HttpResponseMessage response = await driver.PressAsync("[data-block]");
        IDocument page = await new FormClient(client).GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal("These seats were already blocked. Nothing was changed.", page.QuerySelector("[data-flash]")!.TextContent);
    }

    [Fact]
    public async Task Block_SeatNotOnTrip_NamesItAndLoadsSeatsAgain()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.BlocksPath, () => SeatStubs.SeatsProblem(HttpStatusCode.UnprocessableEntity, "UNKNOWN_SEATS", "unknownSeats", "Z9"));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.AvailableField, "L1", true);

        // Act
        await driver.PressAsync("[data-block]");

        // Assert
        Assert.Equal("Some seats are not on this trip: Z9. Nothing was changed.", driver.Text("[data-page-alert]"));
        Assert.Equal(2, host.SeatInventoryCalls(HttpMethod.Get, SeatStubs.AllocationsPath));
    }

    [Theory]
    [InlineData("TRIP_CANCELLED", "This trip was cancelled. Nothing was changed.")]
    [InlineData("TRIP_DEPARTED", "This trip has already left. Nothing was changed.")]
    public async Task Block_TripCancelledOrLeftMeanwhile_SaysSoAndLoadsSeatsAgain(string code, string message)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.BlocksPath, () => IdentityStubs.Problem(HttpStatusCode.Conflict, code));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.AvailableField, "L1", true);

        // Act
        await driver.PressAsync("[data-block]");

        // Assert
        Assert.Equal(message, driver.Text("[data-page-alert]"));
        Assert.Equal(2, host.SeatInventoryCalls(HttpMethod.Get, SeatStubs.AllocationsPath));
    }

    [Fact]
    public async Task Block_RequestRefused_ShowsTitleAndLoadsSeatsAgain()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.BlocksPath, () => IdentityStubs.ValidationProblem("seatNos[0]", "Seat labels must not be empty."));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.AvailableField, "L1", true);

        // Act
        await driver.PressAsync("[data-block]");

        // Assert
        Assert.Equal("One or more validation errors occurred.", driver.Text("[data-page-alert]"));
        Assert.NotNull(driver.Page.QuerySelector("[data-deck]"));
    }

    [Fact]
    public async Task Block_TripNoLongerKnown_ShowsUnavailable()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.BlocksPath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.AvailableField, "L1", true);

        // Act
        await driver.PressAsync("[data-block]");

        // Assert
        Assert.StartsWith(Messages.ProblemSeatDetailsUnavailable, driver.Text("[data-page-alert]"));
        Assert.Null(driver.Page.QuerySelector("[data-deck]"));
    }

    private static async Task<BusFormDriver> OpenAsync(HttpClient client)
    {
        IDocument page = await new FormClient(client).GetPageAsync(SeatStubs.OperatorPage);
        return new BusFormDriver(client, page);
    }

    private static object Blocked(params string[] seatNos)
    {
        return new { tripId = SeatStubs.TripId, seatNos, blockedSeatNos = seatNos };
    }
}
