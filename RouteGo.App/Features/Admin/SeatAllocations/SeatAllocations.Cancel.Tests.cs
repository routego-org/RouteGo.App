using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Admin.SeatAllocations;

public sealed class SeatAllocationsCancelTests
{
    private const string Reason = "Expiry stuck, ticket #4521";

    private static readonly string _questionAddress = SeatStubs.AdminPage + "&cancel=" + SeatStubs.StuckBookingId;

    [Theory]
    [InlineData(false, false, "Hold cancelled. Seats 3A, 3B are back on sale.")]
    [InlineData(true, false, "Hold cancelled. Seats 3A, 3B are now blocked because the trip was cancelled.")]
    [InlineData(false, true, "Hold cancelled. Seats 3A, 3B are free again.")]
    public async Task Post_ValidReason_CancelsHoldAndSaysWhatHappened(bool isCancelled, bool isDeparted, string flash)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.StaffAllocations(isCancelled, isDeparted)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.CancelHoldPath, () => FakeBackend.Json(Released()));
        HttpClient client = await host.SignInClientAsync("Admin");
        var form = new FormClient(client);
        IDocument question = await form.GetPageAsync(_questionAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(question, "[data-cancel-form]", Fields(Reason));
        IDocument page = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(SeatStubs.AdminPage, response.Headers.Location!.OriginalString);
        Assert.Equal("{\"note\":\"Expiry stuck, ticket #4521\"}", host.SeatInventory.LastRequestBody);
        Assert.Equal(flash, page.QuerySelector("[data-flash]")!.TextContent);
    }

    [Fact]
    public async Task Post_ReasonTooShort_KeepsQuestionWithReasonAndSendsNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.StaffAllocations(false, false)));
        HttpClient client = await host.SignInClientAsync("Admin");
        var form = new FormClient(client);
        IDocument question = await form.GetPageAsync(_questionAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(question, "[data-cancel-form]", Fields("stuck"));
        IDocument page = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Messages.ValidationReasonLength, page.QuerySelector("[data-reason-error]")!.TextContent);
        Assert.Equal("stuck", page.QuerySelector("[data-reason]")!.TextContent);
        Assert.NotNull(page.QuerySelector("[data-confirm]"));
        Assert.Equal(0, host.SeatInventoryCalls(HttpMethod.Post, SeatStubs.CancelHoldPath));
    }

    [Fact]
    public async Task Post_SeatInventoryRefusesReason_KeepsQuestionWithReasonMessage()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.StaffAllocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.CancelHoldPath, () => IdentityStubs.ValidationProblem("note", "note must be 10 to 500 characters."));
        HttpClient client = await host.SignInClientAsync("Admin");
        var form = new FormClient(client);
        IDocument question = await form.GetPageAsync(_questionAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(question, "[data-cancel-form]", Fields(Reason));
        IDocument page = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(Messages.ValidationReasonLength, page.QuerySelector("[data-reason-error]")!.TextContent);
        Assert.Equal(Reason, page.QuerySelector("[data-reason]")!.TextContent);
    }

    [Fact]
    public async Task Post_HoldEndedMeanwhile_SaysNothingChangedAndLoadsSeatsAgain()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.StaffAllocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.CancelHoldPath, () => IdentityStubs.Problem(HttpStatusCode.Conflict, "HOLD_NOT_ACTIVE"));
        HttpClient client = await host.SignInClientAsync("Admin");
        var form = new FormClient(client);
        IDocument question = await form.GetPageAsync(_questionAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(question, "[data-cancel-form]", Fields(Reason));
        IDocument page = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal("This booking no longer holds any seats on this trip. Nothing was changed.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-confirm]"));
        Assert.Equal(3, host.SeatInventoryCalls(HttpMethod.Get, SeatStubs.AllocationsPath));
    }

    [Fact]
    public async Task Post_CustomerPaidMeanwhile_NamesSeatsAndLoadsSeatsAgain()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.StaffAllocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.CancelHoldPath, () => SeatStubs.SeatsProblem(HttpStatusCode.Conflict, "HOLD_ALREADY_CONFIRMED", "seatNos", "3A", "3B"));
        HttpClient client = await host.SignInClientAsync("Admin");
        var form = new FormClient(client);
        IDocument question = await form.GetPageAsync(_questionAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(question, "[data-cancel-form]", Fields(Reason));
        IDocument page = await form.ReadDocumentAsync(response);

        // Assert
        Assert.StartsWith("The customer has paid for seats 3A, 3B. Nothing was changed. A paid booking can only be cancelled through Booking.", page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal($"/admin/bookings/{SeatStubs.StuckBookingId}", page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Equal($"Open booking {SeatStubs.StuckBookingId}", page.QuerySelector("[data-page-alert] a")!.TextContent);
        Assert.Equal(3, host.SeatInventoryCalls(HttpMethod.Get, SeatStubs.AllocationsPath));
    }

    [Fact]
    public async Task Post_SeatInventoryUnreachable_KeepsQuestionWithTypedReason()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.StaffAllocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.CancelHoldPath, () => throw new HttpRequestException("connection refused"));
        HttpClient client = await host.SignInClientAsync("Admin");
        var form = new FormClient(client);
        IDocument question = await form.GetPageAsync(_questionAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(question, "[data-cancel-form]", Fields(Reason));
        IDocument page = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal(Reason, page.QuerySelector("[data-reason]")!.TextContent);
    }

    [Fact]
    public async Task Post_TripNoLongerKnown_ShowsUnavailable()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.SeatInventory.Map(HttpMethod.Get, SeatStubs.AllocationsPath, () => FakeBackend.Json(SeatStubs.StaffAllocations(false, false)));
        host.SeatInventory.Map(HttpMethod.Post, SeatStubs.CancelHoldPath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        HttpClient client = await host.SignInClientAsync("Admin");
        var form = new FormClient(client);
        IDocument question = await form.GetPageAsync(_questionAddress);

        // Act
        HttpResponseMessage response = await form.SendFormAsync(question, "[data-cancel-form]", Fields(Reason));
        IDocument page = await form.ReadDocumentAsync(response);

        // Assert
        Assert.StartsWith(Messages.ProblemSeatDetailsUnavailable, page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Null(page.QuerySelector("[data-deck]"));
    }

    private static Dictionary<string, string> Fields(string reason)
    {
        return new Dictionary<string, string>
        {
            ["CancelInput.BookingId"] = SeatStubs.StuckBookingId.ToString(),
            ["CancelInput.Reason"] = reason,
        };
    }

    private static object Released()
    {
        return new { tripId = SeatStubs.TripId, bookingId = SeatStubs.StuckBookingId, releasedSeatNos = new[] { "3A", "3B" } };
    }
}
