using System.Net;
using AngleSharp.Dom;
using static RouteGo.App.Tests.Testing.BusPhotoStubs;

namespace RouteGo.App.Features.Operator.BusPhotos;

public sealed class BusPhotosOrderTests
{
    [Fact]
    public async Task Post_MakeCoverPhoto_SendsTheWholeNewOrderAndSaysSaved()
    {
        // Arrange
        using var host = new FleetTestHost();
        int[] photos = new[] { 1, 2, 3 };
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(photos));
        host.Fleet.Map(HttpMethod.Put, OrderPath, () => PhotoList(3, 1, 2));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        var driver = new BusFormDriver(client, await new FormClient(client).GetPageAsync(PageAddress));

        // Act
        HttpResponseMessage response = await driver.PressAsync($"[data-photo='{Photo(3)}'] [data-move='cover']");
        photos = new[] { 3, 1, 2 };
        IDocument result = await new FormClient(client).GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(PageAddress, response.Headers.Location!.OriginalString);
        Assert.Equal($"{{\"photoIds\":[\"{Photo(3)}\",\"{Photo(1)}\",\"{Photo(2)}\"]}}", host.Fleet.RequestBodies.Single());
        Assert.Equal("Photo order saved.", Text(result, "[data-flash]"));
        Assert.NotNull(result.QuerySelector($"[data-photo='{Photo(3)}'] [data-cover]"));
    }

    [Theory]
    [InlineData(1, "later", new[] { 2, 1, 3 })]
    [InlineData(3, "earlier", new[] { 1, 3, 2 })]
    public async Task Post_MovePhoto_SendsTheOrderWithThatPhotoMovedOnePlace(int photo, string move, int[] expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2, 3));
        host.Fleet.Map(HttpMethod.Put, OrderPath, () => PhotoList(expected));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        var driver = new BusFormDriver(client, await new FormClient(client).GetPageAsync(PageAddress));

        // Act
        await driver.PressAsync($"[data-photo='{Photo(photo)}'] [data-move='{move}']");

        // Assert
        string[] order = expected.Select(number => $"\"{Photo(number)}\"").ToArray();
        Assert.Equal($"{{\"photoIds\":[{string.Join(",", order)}]}}", host.Fleet.RequestBodies.Single());
    }

    [Fact]
    public async Task Post_OrderAfterPhotosChangedElsewhere_SaysChangedAndReadsThemAgain()
    {
        // Arrange
        using var host = new FleetTestHost();
        int[] photos = new[] { 1, 2 };
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(photos));
        host.Fleet.Map(HttpMethod.Put, OrderPath, () => FakeBackend.ValidationError("photoIds", "Every uploaded photo must be named exactly once."));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        var driver = new BusFormDriver(client, await new FormClient(client).GetPageAsync(PageAddress));
        photos = new[] { 1 };

        // Act
        HttpResponseMessage response = await driver.PressAsync($"[data-photo='{Photo(2)}'] [data-move='cover']");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("The photos changed since you opened this page. We have loaded them again.", Text(driver.Page, "[data-page-alert]"));
        Assert.Equal(2, host.FleetCalls(HttpMethod.Get, BusPath));
        Assert.Equal(new[] { Photo(1).ToString() }, PhotoIds(driver.Page));
    }

    [Fact]
    public async Task Post_OrderForBusNotFound_ShowsNotFoundWithLinkBackAndNoPhotos()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        host.Fleet.Map(HttpMethod.Put, OrderPath, () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        var driver = new BusFormDriver(client, await new FormClient(client).GetPageAsync(PageAddress));

        // Act
        await driver.PressAsync($"[data-photo='{Photo(2)}'] [data-move='cover']");

        // Assert
        Assert.StartsWith("We couldn't find that bus. It may have been removed.", Text(driver.Page, "[data-page-alert]"));
        Assert.Equal("/operator/buses", driver.Page.QuerySelector("[data-page-alert] a")!.GetAttribute("href"));
        Assert.Empty(PhotoIds(driver.Page));
    }

    [Fact]
    public async Task Post_OrderFleetUnreachable_ShowsUpstreamAndThePhotosAsTheyAre()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        host.Fleet.Map(HttpMethod.Put, OrderPath, () => throw new HttpRequestException("connection refused"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        var driver = new BusFormDriver(client, await new FormClient(client).GetPageAsync(PageAddress));

        // Act
        await driver.PressAsync($"[data-photo='{Photo(2)}'] [data-move='cover']");

        // Assert
        Assert.Equal(Messages.ProblemUpstream, Text(driver.Page, "[data-page-alert]"));
        Assert.Equal(new[] { Photo(1).ToString(), Photo(2).ToString() }, PhotoIds(driver.Page));
    }

    private static string Text(IDocument page, string selector)
    {
        return page.QuerySelector(selector)!.TextContent.Trim();
    }

    private static string[] PhotoIds(IDocument page)
    {
        return page.QuerySelectorAll("[data-photos] [data-photo]").Select(photo => photo.GetAttribute("data-photo")!).ToArray();
    }
}
