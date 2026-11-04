using RouteGo.App.Infrastructure.Pipeline;

namespace RouteGo.App.Tests.Foundation;

public sealed class SessionRulesTests
{
    [Theory]
    [InlineData("/profile", true)]
    [InlineData("/admin/users?page=2", true)]
    [InlineData("/", true)]
    [InlineData("https://evil.example/steal", false)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("", false)]
    public void IsPortalPath_ReturnAddress_AcceptsOnlyPortalPaths(string returnUrl, bool expected)
    {
        // Act
        bool isPortalPath = SignInAddress.IsPortalPath(returnUrl);

        // Assert
        Assert.Equal(expected, isPortalPath);
    }

    [Theory]
    [InlineData("/Home/Home/Home", PageAccessLevel.Everyone)]
    [InlineData("/Auth/Verify/Verify", PageAccessLevel.Everyone)]
    [InlineData("/Buses/PublicBus/PublicBus", PageAccessLevel.Everyone)]
    [InlineData("/Seats/PublicSeatMap/PublicSeatMap", PageAccessLevel.Everyone)]
    [InlineData("/Search/BusSearch/BusSearch", PageAccessLevel.Everyone)]
    [InlineData("/Operator/SeatBlocking/SeatBlocking", PageAccessLevel.OperatorOnly)]
    [InlineData("/Admin/SeatAllocations/SeatAllocations", PageAccessLevel.AdminOnly)]
    [InlineData("/Auth/Login/Login", PageAccessLevel.VisitorsOnly)]
    [InlineData("/Operator/Reapply/Reapply", PageAccessLevel.VisitorsOnly)]
    [InlineData("/Operator/Apply/Apply", PageAccessLevel.VisitorsOnly)]
    [InlineData("/Operator/Buses/Buses", PageAccessLevel.OperatorOnly)]
    [InlineData("/Operator/Trips/Trips", PageAccessLevel.OperatorOnly)]
    [InlineData("/Admin/Trips/Trips", PageAccessLevel.AdminOnly)]
    [InlineData("/Profile/Profile/Profile", PageAccessLevel.SignedIn)]
    [InlineData("/Auth/Logout/Logout", PageAccessLevel.SignedIn)]
    [InlineData("/Admin/Users/Users", PageAccessLevel.AdminOnly)]
    [InlineData("/Bookings/MyBookings/MyBookings", PageAccessLevel.CustomerOnly)]
    [InlineData("/Bookings/BookSeats/BookSeats", PageAccessLevel.CustomerOnly)]
    [InlineData("/Tickets/Ticket/Ticket", PageAccessLevel.CustomerOrAdmin)]
    [InlineData("/Operator/Dashboard/Dashboard", PageAccessLevel.OperatorOnly)]
    [InlineData("/Admin/Bookings/Bookings", PageAccessLevel.AdminOnly)]
    [InlineData("/Something/New/New", PageAccessLevel.SignedIn)]
    public void LevelOf_PageName_FollowsPageAccessTable(string pageName, PageAccessLevel expected)
    {
        // Act
        PageAccessLevel level = PageAccessFilter.LevelOf(pageName);

        // Assert
        Assert.Equal(expected, level);
    }

    [Fact]
    public void Format_UtcMoment_ShowsIndiaStandardTime()
    {
        // Arrange
        var moment = new DateTimeOffset(2026, 10, 3, 10, 30, 0, TimeSpan.Zero);

        // Act
        string text = IndiaTime.Format(moment);

        // Assert
        Assert.Equal("3 Oct 2026, 4:00 PM IST", text);
    }
}
