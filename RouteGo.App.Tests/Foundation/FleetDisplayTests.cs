namespace RouteGo.App.Tests.Foundation;

public sealed class FleetDisplayTests
{
    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(45, "45 min")]
    [InlineData(110, "1 h 50 min")]
    [InlineData(210, "3 h 30 min")]
    [InlineData(180, "3 h")]
    public void Duration_Minutes_ShowsHoursAndMinutes(int minutes, string expected)
    {
        // Act
        string text = FleetDisplay.Duration(minutes);

        // Assert
        Assert.Equal(expected, text);
    }

    [Fact]
    public void RouteName_TwoStops_NamesFirstAndLastCity()
    {
        // Arrange
        string[] cities = new[] { "Pune", "Nashik" };

        // Act
        string name = FleetDisplay.RouteName(cities);

        // Assert
        Assert.Equal("Pune → Nashik", name);
    }

    [Fact]
    public void RouteName_StopsInBetween_ListsThemAfterVia()
    {
        // Arrange
        string[] cities = new[] { "Mumbai", "Lonavala", "Khandala", "Pune" };

        // Act
        string name = FleetDisplay.RouteName(cities);

        // Assert
        Assert.Equal("Mumbai → Pune via Lonavala, Khandala", name);
    }
}
