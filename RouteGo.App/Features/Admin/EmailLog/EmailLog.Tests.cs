using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RouteGo.App.Common.Ports;
using RouteGo.App.Infrastructure.Pipeline;

namespace RouteGo.App.Features.Admin.EmailLog;

public sealed class EmailLogTests
{
    [Fact]
    public async Task Get_FirstVisit_ShowsSearchFormWithoutCallingNotification()
    {
        // Arrange
        var host = new EmailLogTestHost();
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(EmailLogTestData.EmailLogPath);

        // Assert
        Assert.Equal("Email log", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("Enter an email address to see every email RouteGo tried to send to it.", page.QuerySelector("[data-email-log-intro]")!.TextContent);
        Assert.Equal("Email address", page.QuerySelector("[data-email-log-search] label")!.TextContent);
        Assert.Equal("Search", page.QuerySelector("[data-email-log-search] button")!.TextContent);
        Assert.Empty(host.Notification.Requests);
    }

    [Fact]
    public async Task Get_SearchAddress_SendsItWithFirstPageAndShowsEveryColumn()
    {
        // Arrange
        var host = new EmailLogTestHost();
        host.Notification.Map(HttpMethod.Get, EmailLogTestData.SearchPath, () => EmailLogTestData.Answer(2, 1,
            EmailLogTestData.Entry(EmailLogTestData.ResentId, "sent", null, EmailLogTestData.FailedId),
            EmailLogTestData.Entry(EmailLogTestData.FailedId, "failed", "refusedByEmailService", null)));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);
        string[] columns = new[] { "Tried at", "Email type", "Subject", "Status", "Failure reason", "Failure detail", "Receipt number", "Sent at", "" };

        // Act
        IDocument page = await form.GetPageAsync(EmailLogTestData.EmailLogPath + "?email=Ravi%40Gmail.com");

        // Assert
        Assert.Equal("?toAddress=Ravi@Gmail.com&page=1&pageSize=20", EmailLogTestData.LastSearchQuery(host.Notification));
        Assert.Equal(TestHost.TestInternalKey, host.Notification.LastRequestHeader(InternalKeyHandler.HeaderName));
        Assert.Equal("Emails sent to Ravi@Gmail.com", page.QuerySelector("[data-email-log-heading]")!.TextContent);
        Assert.Equal("Showing 1–2 of 2 emails", page.QuerySelector("[data-email-log-showing]")!.TextContent);
        Assert.Equal(columns, EmailLogTestData.Texts(page, "[data-email-log-table] th"));
        IElement failed = EmailLogTestData.Row(page, EmailLogTestData.FailedId);
        Assert.Equal("2 Oct 2026, 3:30 PM IST", failed.QuerySelectorAll("td")[0].TextContent.Trim());
        Assert.Equal("Operator approved", failed.QuerySelectorAll("td")[1].TextContent.Trim());
        Assert.StartsWith("Your RouteGo operator account is approved", failed.QuerySelectorAll("td")[2].TextContent.Trim(), StringComparison.Ordinal);
        Assert.Equal("Failed", failed.QuerySelector("[data-entry-status]")!.TextContent);
        Assert.Equal("Refused by the email service", failed.QuerySelector("[data-entry-failure-reason]")!.TextContent);
        Assert.Equal("Email address is not verified.", failed.QuerySelectorAll("td")[5].TextContent.Trim());
        Assert.Equal("Event ID: evt-1", failed.QuerySelector("[data-entry-event-id]")!.TextContent);
        Assert.Equal("Correlation ID: corr-1", failed.QuerySelector("[data-entry-correlation-id]")!.TextContent);
        Assert.Null(failed.QuerySelector("[data-entry-resend-of]"));
        Assert.Null(page.QuerySelector("[data-email-log-pages]"));
    }

    [Fact]
    public async Task Get_SentResend_ShowsMarkerReceiptAndSentAt()
    {
        // Arrange
        var host = new EmailLogTestHost();
        host.Notification.Map(HttpMethod.Get, EmailLogTestData.SearchPath, () => EmailLogTestData.Answer(1, 1,
            EmailLogTestData.Entry(EmailLogTestData.ResentId, "sent", null, EmailLogTestData.FailedId)));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(EmailLogTestData.EmailLogPath + "?email=owner%40busco.in");

        // Assert
        IElement resent = EmailLogTestData.Row(page, EmailLogTestData.ResentId);
        Assert.Equal("Sent", resent.QuerySelector("[data-entry-status]")!.TextContent);
        Assert.Equal("Resend of an earlier email", resent.QuerySelector("[data-entry-resend-of]")!.TextContent);
        Assert.Equal("ses-receipt-1", resent.QuerySelectorAll("td")[6].TextContent.Trim());
        Assert.Equal("2 Oct 2026, 3:31 PM IST", resent.QuerySelectorAll("td")[7].TextContent.Trim());
    }

    [Fact]
    public async Task Get_NoEmailsForAddress_ShowsEmptyMessage()
    {
        // Arrange
        var host = new EmailLogTestHost();
        host.Notification.Map(HttpMethod.Get, EmailLogTestData.SearchPath, () => EmailLogTestData.Answer(0, 1));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(EmailLogTestData.EmailLogPath + "?email=nobody%40example.com");

        // Assert
        Assert.Equal("No emails were sent to nobody@example.com.", page.QuerySelector("[data-email-log-empty]")!.TextContent);
        Assert.Null(page.QuerySelector("[data-email-log-table]"));
    }

    [Fact]
    public async Task Get_EntriesWithNothingToSend_OfferNoResend()
    {
        // Arrange
        var host = new EmailLogTestHost();
        var noRecipient = Guid.NewGuid();
        var badData = Guid.NewGuid();
        var unavailable = Guid.NewGuid();
        host.Notification.Map(HttpMethod.Get, EmailLogTestData.SearchPath, () => EmailLogTestData.Answer(4, 1,
            EmailLogTestData.Entry(noRecipient, "failed", "missingRecipient", null),
            EmailLogTestData.Entry(badData, "failed", "invalidEventData", null),
            EmailLogTestData.Entry(unavailable, "failed", "emailServiceUnavailable", null),
            EmailLogTestData.Entry(EmailLogTestData.ResentId, "sent", null, null)));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(EmailLogTestData.EmailLogPath + "?email=owner%40busco.in");

        // Assert
        Assert.Null(EmailLogTestData.Row(page, noRecipient).QuerySelector("[data-resend]"));
        Assert.Null(EmailLogTestData.Row(page, badData).QuerySelector("[data-resend]"));
        Assert.Equal("Resend", EmailLogTestData.Row(page, unavailable).QuerySelector("[data-resend]")!.TextContent);
        Assert.NotNull(EmailLogTestData.Row(page, EmailLogTestData.ResentId).QuerySelector("[data-resend]"));
    }

    [Theory]
    [InlineData("not-an-email", "Enter a valid email address.")]
    [InlineData("", "Email address is required.")]
    public async Task Get_MalformedOrEmptyAddress_ShowsFieldMessageWithoutCallingNotification(string email, string expected)
    {
        // Arrange
        var host = new EmailLogTestHost();
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(EmailLogTestData.EmailLogPath + "?email=" + email);

        // Assert
        Assert.Equal(expected, page.QuerySelector("[data-valmsg-for='Query.Email']")!.TextContent.Trim());
        Assert.Empty(host.Notification.Requests);
    }

    [Fact]
    public async Task Get_WrongInternalKey_ShowsKeyProblemAndKeepsAdminSignedIn()
    {
        // Arrange
        var host = new EmailLogTestHost();
        host.Notification.Map(HttpMethod.Get, EmailLogTestData.SearchPath, () => IdentityStubs.Problem(HttpStatusCode.Unauthorized, "UNAUTHORIZED"));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(EmailLogTestData.EmailLogPath + "?email=ravi%40gmail.com");
        IDocument nextPage = await form.GetPageAsync(EmailLogTestData.EmailLogPath);

        // Assert
        Assert.Equal(
            "The email log is not set up correctly: the portal cannot connect to the Notification service. Tell the platform engineer.",
            page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.NotNull(nextPage.QuerySelector("[data-signed-in-as]"));
        Assert.DoesNotContain(host.Identity.Requests, request => request.RequestUri!.AbsolutePath == "/auth/sessions/refresh");
    }

    [Fact]
    public async Task Get_NotificationUnreachable_ShowsUpstreamMessageKeepsAddress()
    {
        // Arrange
        var host = new EmailLogTestHost();
        host.Notification.Map(HttpMethod.Get, EmailLogTestData.SearchPath, () => throw new HttpRequestException("connection refused"));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(EmailLogTestData.EmailLogPath + "?email=ravi%40gmail.com");

        // Assert
        Assert.Equal(
            "This part of RouteGo is not available right now. Please try again in a few minutes.",
            page.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal("ravi@gmail.com", page.QuerySelector("#search-email")!.GetAttribute("value"));
        Assert.Null(page.QuerySelector("[data-email-log-table]"));
    }

    [Fact]
    public async Task Get_NotificationRefusesAddress_ShowsFormatMessageOnField()
    {
        // Arrange
        var host = new EmailLogTestHost();
        host.Notification.Map(HttpMethod.Get, EmailLogTestData.SearchPath, () => IdentityStubs.ValidationProblem("toAddress", "'To Address' is not a valid email address."));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(EmailLogTestData.EmailLogPath + "?email=ravi%40gmail.com");

        // Assert
        Assert.Equal("Enter a valid email address.", page.QuerySelector("[data-valmsg-for='Query.Email']")!.TextContent.Trim());
    }

    [Fact]
    public async Task Get_SecondPage_SendsPageAndShowsRange()
    {
        // Arrange
        var host = new EmailLogTestHost();
        host.Notification.Map(HttpMethod.Get, EmailLogTestData.SearchPath, () => EmailLogTestData.Answer(45, 2, EmailLogTestData.Entries(20)));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);

        // Act
        IDocument page = await form.GetPageAsync(EmailLogTestData.EmailLogPath + "?email=ravi%40gmail.com&pageNumber=2");

        // Assert
        Assert.Equal("?toAddress=ravi@gmail.com&page=2&pageSize=20", EmailLogTestData.LastSearchQuery(host.Notification));
        Assert.Equal("Showing 21–40 of 45 emails", page.QuerySelector("[data-email-log-showing]")!.TextContent);
        Assert.Equal(new[] { "1", "2", "3" }, EmailLogTestData.Texts(page, "[data-email-log-pages] a"));
        Assert.Equal("/admin/email-log?email=ravi@gmail.com&pageNumber=3", page.QuerySelectorAll("[data-email-log-pages] a")[2].GetAttribute("href"));
    }

    [Fact]
    public async Task Get_Customer_ShowsNoPermissionWithoutCallingNotification()
    {
        // Arrange
        var host = new EmailLogTestHost();
        HttpClient client = TestHost.CreateClient(host.Factory);
        await TestSignIn.SignInAsync(client, host.Identity, IdentityStubs.Tokens("alice@example.com", PortalSession.RoleCustomer));

        // Act
        HttpResponseMessage response = await client.GetAsync(EmailLogTestData.EmailLogPath + "?email=ravi%40gmail.com");
        string html = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("You don&#x27;t have permission to open this page.", html);
        Assert.Empty(host.Notification.Requests);
    }

    [Fact]
    public async Task Get_Visitor_IsSentToSignInWithReturnAddress()
    {
        // Arrange
        var host = new EmailLogTestHost();
        HttpClient client = TestHost.CreateClient(host.Factory);

        // Act
        HttpResponseMessage response = await client.GetAsync(EmailLogTestData.EmailLogPath);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/login?returnUrl=%2Fadmin%2Femail-log&reason=sign-in-required", response.Headers.Location!.PathAndQuery);
    }
}

/// <summary>
/// A test host whose Identity and Notification clients are separate fakes, and whose log lines are
/// collected, so a test can tell which service was called and what the portal logged.
/// </summary>
internal sealed class EmailLogTestHost
{
    public EmailLogTestHost()
    {
        Factory = TestHost.CreateFactory(services =>
        {
            services.AddHttpClient(HttpClients.Identity).ConfigurePrimaryHttpMessageHandler(() => Identity);
            services.AddHttpClient(HttpClients.Notification).ConfigurePrimaryHttpMessageHandler(() => Notification);
            services.AddSingleton<IClock, FixedClock>();
            services.AddSingleton<ILoggerProvider>(Log);
        });
    }

    public FakeBackend Identity { get; } = new FakeBackend();

    public FakeBackend Notification { get; } = new FakeBackend();

    public LogCollector Log { get; } = new LogCollector();

    public WebApplicationFactory<Program> Factory { get; }

    /// <summary>The signed-in person's account identifier (the access token's sub).</summary>
    public string? UserId { get; private set; }

    public async Task<FormClient> SignInAsync(string role)
    {
        HttpClient client = TestHost.CreateClient(Factory);
        TokenPair tokens = IdentityStubs.Tokens("priya@example.com", role);
        UserId = EmailLogTestData.SubjectOf(tokens.AccessToken);
        await TestSignIn.SignInAsync(client, Identity, tokens);
        return new FormClient(client);
    }
}

/// <summary>Collects every formatted log line, so a test can assert what the portal wrote.</summary>
internal sealed class LogCollector : ILoggerProvider, ILogger
{
    private readonly ConcurrentQueue<string> _lines = new ConcurrentQueue<string>();

    public IReadOnlyCollection<string> Lines
    {
        get { return _lines.ToArray(); }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return this;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _lines.Enqueue(formatter(state, exception));
    }

    public void Dispose()
    {
    }
}

/// <summary>Notification's send log answers and the steps every email log test shares.</summary>
internal static class EmailLogTestData
{
    public const string EmailLogPath = "/admin/email-log";
    public const string SearchPath = "/internal/sendLogEntries";

    public static readonly Guid FailedId = new Guid("44444444-4444-4444-4444-444444444444");
    public static readonly Guid ResentId = new Guid("55555555-5555-5555-5555-555555555555");

    /// <summary>One entry as Notification's search lists it, an "Operator approved" email to owner@busco.in.</summary>
    public static object Entry(Guid id, string status, string? failureReason, Guid? resendOf)
    {
        string? failureDetail = null;
        if (failureReason != null)
        {
            failureDetail = "Email address is not verified.";
        }

        string? receipt = null;
        string? sentAt = null;
        if (status == "sent")
        {
            receipt = "ses-receipt-1";
            sentAt = "2026-10-02T10:01:00Z";
        }

        return new
        {
            sendLogEntryId = id,
            eventId = "evt-1",
            eventType = "operatorApproved",
            emailType = "operatorApproved",
            toAddress = "owner@busco.in",
            subject = "Your RouteGo operator account is approved",
            status,
            failureReason,
            failureDetail,
            providerMessageId = receipt,
            resendOf,
            eventOccurredAt = "2026-10-02T09:59:00Z",
            correlationId = "corr-1",
            attemptedAt = "2026-10-02T10:00:00Z",
            sentAt,
        };
    }

    /// <summary>A given number of sent entries, for a full page.</summary>
    public static object[] Entries(int count)
    {
        var entries = new List<object>();
        for (int index = 0; index < count; index++)
        {
            entries.Add(Entry(Guid.NewGuid(), "sent", null, null));
        }

        return entries.ToArray();
    }

    public static HttpResponseMessage Answer(int totalCount, int page, params object[] items)
    {
        return FakeBackend.Json(new { items, totalCount, page, pageSize = 20 }, HttpStatusCode.OK);
    }

    public static string LastSearchQuery(FakeBackend notification)
    {
        HttpRequestMessage search = notification.Requests.Last(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == SearchPath);
        return search.RequestUri!.Query;
    }

    public static IElement Row(IDocument page, Guid sendLogEntryId)
    {
        return page.QuerySelector($"[data-entry-row='{sendLogEntryId}']")!;
    }

    public static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }

    /// <summary>The sub claim of a test access token — the account identifier the portal logs.</summary>
    public static string SubjectOf(string accessToken)
    {
        string payload = accessToken.Split('.')[1];
        using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(payload));
        return claims.RootElement.GetProperty("sub").GetString()!;
    }
}
