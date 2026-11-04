using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Error;

public sealed class ErrorTests
{
    [Theory]
    [InlineData("/no-such-page")]
    [InlineData("/my-bookings/not-a-real-page/extra")]
    [InlineData("/admin/no-such-page")]
    public async Task Get_UnknownAddressAsVisitor_Shows404WithoutSignInOrServiceCall(string address)
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(address);
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(Messages.ProblemPageNotFound, page.QuerySelector("[data-problem='page-not-found']")!.TextContent);
        Assert.Equal("/", page.QuerySelector("[data-error-page] a")!.GetAttribute("href"));
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Get_UnknownAddressAsCustomer_Shows404AndTheRoleMenu()
    {
        // Arrange
        var fake = new FakeBackend();
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        HttpClient client = TestHost.CreateClient(factory);
        await TestSignIn.SignInAsync(client, fake, IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer));
        int callsAfterSignIn = fake.Requests.Count;

        // Act
        HttpResponseMessage response = await client.GetAsync("/no-such-page");
        IDocument page = await new FormClient(client).ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(page.QuerySelector("[data-problem='page-not-found']"));
        Assert.NotNull(page.QuerySelector("[data-role-menu]"));
        Assert.Equal(callsAfterSignIn, fake.Requests.Count);
    }

    [Fact]
    public async Task Get_ErrorPageDirectly_Shows500WithTheCorrelationId()
    {
        // Arrange
        using WebApplicationFactory<Program> factory = TestHost.CreateFactory(new FakeBackend());
        HttpClient client = TestHost.CreateClient(factory);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "trace-abc-123");

        // Act
        HttpResponseMessage response = await client.GetAsync("/error");
        string body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("contact support with reference trace-abc-123", body);
        Assert.Contains("Go to Home", body);
    }
}
