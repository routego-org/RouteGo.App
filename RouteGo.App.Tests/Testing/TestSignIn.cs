using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RouteGo.App.Common.Ports;

namespace RouteGo.App.Tests.Testing;

/// <summary>
/// Signs a test browser in the way a person does — through the sign-in page — so every signed-in
/// test exercises the real session cookie. Identity's sign-in answer is the given token pair.
/// </summary>
public static class TestSignIn
{
    public const string SessionsPath = "/auth/sessions";

    /// <summary>A host whose Identity client is the fake and whose clock reads <see cref="FixedClock.Now"/>.</summary>
    public static WebApplicationFactory<Program> CreateFactory(FakeBackend fake)
    {
        return TestHost.CreateFactory(services =>
        {
            services.AddHttpClient(HttpClients.Identity).ConfigurePrimaryHttpMessageHandler(() => fake);
            services.AddSingleton<IClock, FixedClock>();
        });
    }

    /// <summary>Signs the client in with the token pair; the fake must not map /auth/sessions itself.</summary>
    public static async Task SignInAsync(HttpClient client, FakeBackend fake, TokenPair tokens)
    {
        fake.Map(HttpMethod.Post, SessionsPath, () => IdentityStubs.TokenAnswer(tokens));
        var form = new FormClient(client);
        IDocument page = await form.GetPageAsync("/auth/login");
        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = "signed-in@example.com",
            ["Input.Password"] = "travel2026x",
        };
        HttpResponseMessage response = await form.SendFormAsync(page, "form[data-login-form]", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
