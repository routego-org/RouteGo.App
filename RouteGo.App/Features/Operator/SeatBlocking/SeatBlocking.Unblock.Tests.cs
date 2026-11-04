using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.SeatBlocking;

public sealed class SeatBlockingUnblockTests
{
    [Fact]
    public async Task Unblock_OwnBlock_AsksFirstAndSendsNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.BlockedField, "U2", true);

        // Act
        HttpResponseMessage response = await driver.PressAsync("[data-unblock]");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Put U2 back on sale? Customers can book it straight away.", driver.Text("[data-question]"));
        Assert.Equal("Unblock seats", driver.Text("[data-confirm-unblock]"));
        Assert.Equal("Go back", driver.Text("[data-go-back]"));
        Assert.Equal(0, host.SeatInventoryCalls(HttpMethod.Delete, SeatStubs.BlocksPath));
    }

    [Fact]
    public async Task Unblock_Confirmed_UnblocksAndSaysSo()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Delete, SeatStubs.BlocksPath, () => new HttpResponseMessage(HttpStatusCode.NoContent));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.BlockedField, "U2", true);
        await driver.PressAsync("[data-unblock]");

        // Act
        HttpResponseMessage response = await driver.PressAsync("[data-confirm-unblock]");
        IDocument page = await new FormClient(client).GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SeatStubs.OperatorPage, response.Headers.Location!.OriginalString);
        Assert.Equal("?seatNos=U2", SeatStubs.UnblockedSeats(host.SeatInventory));
        Assert.Equal("Seats unblocked: U2. They are back on sale.", page.QuerySelector("[data-flash]")!.TextContent);
    }

    [Fact]
    public async Task Unblock_GoBack_SendsNothingAndKeepsSeatsPicked()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.BlockedField, "U2", true);
        await driver.PressAsync("[data-unblock]");

        // Act
        await driver.PressAsync("[data-go-back]");

        // Assert
        Assert.Null(driver.Page.QuerySelector("[data-unblock-question]"));
        Assert.Equal(new[] { "U2" }, SeatStubs.Ticked(driver.Page));
        Assert.Equal(0, host.SeatInventoryCalls(HttpMethod.Delete, SeatStubs.BlocksPath));
    }

    [Fact]
    public async Task Unblock_FreeSeatAndOwnBlockPicked_AsksAboutAndSendsOnlyOwnBlock()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Delete, SeatStubs.BlocksPath, () => new HttpResponseMessage(HttpStatusCode.NoContent));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.AvailableField, "L1", true);
        driver.Tick(SeatBlockingModel.BlockedField, "U2", true);

        // Act
        await driver.PressAsync("[data-unblock]");
        string? question = driver.Text("[data-question]");
        await driver.PressAsync("[data-confirm-unblock]");

        // Assert
        Assert.Equal("Put U2 back on sale? Customers can book it straight away.", question);
        Assert.Equal("?seatNos=U2", SeatStubs.UnblockedSeats(host.SeatInventory));
    }

    [Fact]
    public async Task Unblock_NoOwnBlockPicked_SaysSoAndSendsNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.AvailableField, "L1", true);

        // Act
        await driver.PressAsync("[data-unblock]");

        // Assert
        Assert.Equal("Pick at least one seat you blocked to unblock.", driver.Text("[data-page-alert]"));
        Assert.Null(driver.Page.QuerySelector("[data-unblock-question]"));
        Assert.Equal(0, host.SeatInventoryCalls(HttpMethod.Delete, SeatStubs.BlocksPath));
    }

    [Fact]
    public async Task Unblock_SeatNoLongerOwnBlock_NamesItAndLoadsSeatsAgain()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.Allocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Delete, SeatStubs.BlocksPath, () => SeatStubs.SeatsProblem(HttpStatusCode.Conflict, "SEATS_NOT_OPERATOR_BLOCKED", "seatNos", "U2"));
        HttpClient client = await host.SignInClientAsync("Operator");
        BusFormDriver driver = await OpenAsync(client);
        driver.Tick(SeatBlockingModel.BlockedField, "U2", true);
        await driver.PressAsync("[data-unblock]");

        // Act
        await driver.PressAsync("[data-confirm-unblock]");

        // Assert
        Assert.Equal("These seats are no longer blocked by you: U2. Nothing was unblocked.", driver.Text("[data-page-alert]"));
        Assert.Empty(SeatStubs.Ticked(driver.Page));
        Assert.Equal(3, host.SeatInventoryCalls(HttpMethod.Get, SeatStubs.AllocationsPath));
    }

    private static async Task<BusFormDriver> OpenAsync(HttpClient client)
    {
        IDocument page = await new FormClient(client).GetPageAsync(SeatStubs.OperatorPage);
        return new BusFormDriver(client, page);
    }
}
