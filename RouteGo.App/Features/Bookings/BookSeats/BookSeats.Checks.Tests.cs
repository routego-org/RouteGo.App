using AngleSharp.Dom;

namespace RouteGo.App.Features.Bookings.BookSeats;

public sealed class BookSeatsChecksTests
{
    [Fact]
    public async Task Pay_NoSeatChosen_ShowsChooseAtLeastOneSeatAndSendsNothing()
    {
        // Arrange
        using FleetTestHost host = Host();
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);

        // Act
        IDocument after = await Post(browser, page, BookSeatsStubs.Form());

        // Assert
        Assert.Equal("Choose at least one seat.", BookingStubs.AlertText(after));
        Assert.Equal(0, BookSeatsStubs.BookingPosts(host));
    }

    [Theory]
    [InlineData("Passengers[3A].Name", "R", "3A|name", "Passenger name must be at least 2 characters.")]
    [InlineData("Passengers[3A].Name", "", "3A|name", "Passenger name is required.")]
    [InlineData("Passengers[3A].Age", "121", "3A|age", "Passenger age must be between 0 and 120.")]
    [InlineData("Passengers[3A].Age", "4.5", "3A|age", "Enter a valid passenger age.")]
    [InlineData("Passengers[3A].Age", "", "3A|age", "Passenger age is required.")]
    [InlineData("Passengers[3A].Gender", "", "3A|gender", "Passenger gender is required.")]
    [InlineData("ContactName", "", "contactName", "Contact name is required.")]
    public async Task Pay_FieldFailsItsCheck_ShowsItsMessageUnderTheFieldAndSendsNothing(string field, string value, string key, string message)
    {
        // Arrange
        using FleetTestHost host = Host();
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);
        List<KeyValuePair<string, string>> fields = BookSeatsStubs.With(BookSeatsStubs.Form("3A", "3B"), field, value);

        // Act
        IDocument after = await Post(browser, page, fields);

        // Assert
        Assert.Equal(message, BookSeatsStubs.ErrorOf(after, key));
        Assert.Equal(0, BookSeatsStubs.BookingPosts(host));
        Assert.Equal(new[] { "3A", "3B" }, BookSeatsStubs.Chosen(after));
    }

    [Fact]
    public async Task Pay_LongNames_ShowTheMaximumLengthMessages()
    {
        // Arrange
        using FleetTestHost host = Host();
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);
        List<KeyValuePair<string, string>> fields = BookSeatsStubs.With(BookSeatsStubs.Form("3A"), "ContactName", new string('a', 61));
        fields = BookSeatsStubs.With(fields, "Passengers[3A].Name", new string('b', 61));

        // Act
        IDocument after = await Post(browser, page, fields);

        // Assert
        Assert.Equal("Contact name must be 60 characters or fewer.", BookSeatsStubs.ErrorOf(after, "contactName"));
        Assert.Equal("Passenger name must be 60 characters or fewer.", BookSeatsStubs.ErrorOf(after, "3A|name"));
        Assert.Equal(0, BookSeatsStubs.BookingPosts(host));
    }

    [Fact]
    public async Task Pay_MoreThanSixSeats_SaysThePageAllowsSixAndSendsNothing()
    {
        // Arrange
        string[] free = new[] { "1A", "1B", "1C", "1D", "2A", "2B", "2C", "2D" };
        using FleetTestHost host = BookSeatsStubs.Host(() => BookSeatsStubs.SeatMap(free));
        host.Booking.Map(HttpMethod.Post, BookingStubs.BookingsPath, () => BookSeatsStubs.Created(null));
        FormClient browser = await host.SignInAsync(PortalSession.RoleCustomer);
        IDocument page = await browser.GetPageAsync(BookSeatsStubs.RoutedAddress);
        var fields = new List<KeyValuePair<string, string>>();
        foreach (string seat in free.Take(7))
        {
            fields.Add(BookSeatsStubs.Field("Picks", seat));
        }

        // Act
        IDocument after = await Post(browser, page, fields);

        // Assert
        Assert.Equal("You can book up to 6 seats.", BookingStubs.AlertText(after));
        Assert.Equal(0, BookSeatsStubs.BookingPosts(host));
    }

    private static FleetTestHost Host()
    {
        FleetTestHost host = BookSeatsStubs.Host(() => BookSeatsStubs.SeatMap());
        host.Booking.Map(HttpMethod.Post, BookingStubs.BookingsPath, () => BookSeatsStubs.Created(BookSeatsStubs.CheckoutUrl));
        return host;
    }

    private static async Task<IDocument> Post(FormClient browser, IDocument page, List<KeyValuePair<string, string>> fields)
    {
        HttpResponseMessage response = await browser.SendFormAsync(page, "[data-book-form]", fields);
        return await browser.ReadDocumentAsync(response);
    }
}
