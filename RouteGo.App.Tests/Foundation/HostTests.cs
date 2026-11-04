using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RouteGo.App.Infrastructure;

namespace RouteGo.App.Tests.Foundation;

public sealed class HostTests
{
    [Fact]
    public async Task Get_AnyPage_CarriesSecurityHeaders()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(new FakeBackend());
        HttpClient client = TestHost.CreateClient(factory);

        // Act
        HttpResponseMessage response = await client.GetAsync("/auth/login");

        // Assert
        string policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.Contains("'nonce-", policy);
        Assert.DoesNotContain("fonts.googleapis.com", policy);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Get_AnyPage_AllowsBusPhotoAddressesFromSettings()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(new FakeBackend());
        HttpClient client = TestHost.CreateClient(factory);

        // Act
        HttpResponseMessage response = await client.GetAsync("/auth/login");

        // Assert
        string policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains($"img-src 'self' data: {TestHost.PhotoDeliveryOrigin};", policy);
        Assert.Contains($"connect-src 'self' {TestHost.PhotoUploadOrigin};", policy);
    }

    [Fact]
    public async Task Get_AnyPage_AllowsThePaymentPageAsAFormTargetFromSettings()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(new FakeBackend());
        HttpClient client = TestHost.CreateClient(factory);

        // Act
        HttpResponseMessage response = await client.GetAsync("/auth/login");

        // Assert
        string policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains($"form-action 'self' {TestHost.PaymentPageOrigin};", policy);
    }

    [Theory]
    [InlineData("https://photos.example.com/some/path", "https://photos.example.com")]
    [InlineData("http://localhost:4566", "http://localhost:4566")]
    public void ReadOrigin_WebAddress_ReturnsItsOrigin(string value, string expected)
    {
        // Arrange
        IConfiguration configuration = Settings(value);

        // Act
        string? origin = configuration.ReadOrigin(ConfigKeys.PhotoDeliveryOrigin);

        // Assert
        Assert.Equal(expected, origin);
    }

    [Fact]
    public void ReadOrigin_NotSet_ReturnsNull()
    {
        // Arrange
        IConfiguration configuration = Settings(" ");

        // Act
        string? origin = configuration.ReadOrigin(ConfigKeys.PhotoDeliveryOrigin);

        // Assert
        Assert.Null(origin);
    }

    [Theory]
    [InlineData("photos.example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://photos.example.com")]
    public void ReadOrigin_NotAWebAddress_RefusesToStartNamingTheKey(string value)
    {
        // Arrange
        IConfiguration configuration = Settings(value);

        // Act
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => configuration.ReadOrigin(ConfigKeys.PhotoDeliveryOrigin));

        // Assert
        Assert.Contains(ConfigKeys.PhotoDeliveryOrigin, refusal.Message);
    }

    [Fact]
    public async Task Get_AnyPage_ReturnsCorrelationId()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(new FakeBackend());
        HttpClient client = TestHost.CreateClient(factory);
        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        request.Headers.Add(CorrelationId.HeaderName, "abc-123");

        // Act
        HttpResponseMessage response = await client.SendAsync(request);

        // Assert
        Assert.Equal("abc-123", response.Headers.GetValues(CorrelationId.HeaderName).Single());
    }

    [Fact]
    public async Task Post_WithoutAntiforgeryToken_ShowsFormExpiredOnFreshForm()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        var form = new FormClient(client);
        var fields = new Dictionary<string, string> { ["Input.Email"] = "alice@example.com" };

        // Act
        HttpResponseMessage response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(fields));
        IDocument page = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/login", response.Headers.Location!.OriginalString);
        Assert.Equal("This form has expired. Reload the page and try again.", page.QuerySelector("[data-flash]")!.TextContent);
        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData(HttpClients.Identity)]
    [InlineData(HttpClients.Notification)]
    [InlineData(HttpClients.Fleet)]
    [InlineData(HttpClients.SeatInventory)]
    [InlineData(HttpClients.Search)]
    [InlineData(HttpClients.Booking)]
    [InlineData(HttpClients.Reporting)]
    [InlineData(HttpClients.Audit)]
    public void ServiceClient_PrimaryHandler_KeepsNoCookies(string clientName)
    {
        // Arrange
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(_ => { });
        IHttpMessageHandlerFactory handlers = factory.Services.GetRequiredService<IHttpMessageHandlerFactory>();

        // Act
        SocketsHttpHandler primary = PrimaryHandler(handlers.CreateHandler(clientName));

        // Assert
        Assert.False(primary.UseCookies);
    }

    [Fact]
    public async Task Get_StaticAsset_IsServed()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(new FakeBackend());
        HttpClient client = TestHost.CreateClient(factory);

        // Act
        HttpResponseMessage response = await client.GetAsync("/lib/bootstrap/css/bootstrap.min.css");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static IConfiguration Settings(string photoDeliveryOrigin)
    {
        var values = new Dictionary<string, string?> { [ConfigKeys.PhotoDeliveryOrigin] = photoDeliveryOrigin };
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static SocketsHttpHandler PrimaryHandler(HttpMessageHandler outermost)
    {
        HttpMessageHandler current = outermost;
        while (current is DelegatingHandler delegating)
        {
            current = delegating.InnerHandler!;
        }

        return Assert.IsType<SocketsHttpHandler>(current);
    }
}
