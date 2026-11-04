using System.Net;
using AngleSharp.Dom;

namespace RouteGo.App.Features.Admin.EmailLog;

public sealed class EmailLogActionsTests
{
    private const string ConfirmForm = "form[data-confirm-form]";
    private const string SearchAddress = "?email=owner%40busco.in";

    private static readonly string _resendPath = $"/internal/sendLogEntries/{EmailLogTestData.FailedId}/resend";

    [Fact]
    public async Task Post_ConfirmResend_SendsAgainAndReloadsList()
    {
        // Arrange
        var host = new EmailLogTestHost();
        var log = new OwnerSendLog();
        host.Notification.Map(HttpMethod.Get, EmailLogTestData.SearchPath, log.Search);
        host.Notification.Map(HttpMethod.Post, _resendPath, log.Resend);
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{EmailLogTestData.EmailLogPath}{SearchAddress}&resend={EmailLogTestData.FailedId}");

        // Act
        HttpResponseMessage response = await ConfirmAsync(form, page);
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(
            "Send this email again to owner@busco.in? Subject: Your RouteGo operator account is approved",
            page.QuerySelector("[data-confirm-question]")!.TextContent);
        Assert.Equal("Resend email", page.QuerySelector(ConfirmForm + " button")!.TextContent);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/email-log?email=owner@busco.in&pageNumber=1", response.Headers.Location!.OriginalString);
        Assert.Equal("The email was sent again to owner@busco.in.", result.QuerySelector("[data-flash]")!.TextContent);
        IElement first = result.QuerySelector("[data-entry-row]")!;
        Assert.Equal(EmailLogTestData.ResentId.ToString(), first.GetAttribute("data-entry-row"));
        Assert.Equal("Sent", first.QuerySelector("[data-entry-status]")!.TextContent);
        Assert.Equal("Resend of an earlier email", first.QuerySelector("[data-entry-resend-of]")!.TextContent);
        Assert.Null(result.QuerySelector("[data-confirm]"));
    }

    [Theory]
    [InlineData(422, "RESEND_LINK_EXPIRED", "linkExpiredAt", "2026-10-03T10:15:00Z",
        "This email cannot be resent because its verification link expired on 3 Oct 2026, 3:45 PM IST. Ask the person to use 'Send the verification email again' on the website.")]
    [InlineData(502, "UPSTREAM_UNAVAILABLE", "sendLogEntryId", "66666666-6666-6666-6666-666666666666",
        "The email service is not available, so the email was not sent. The failed try is now in the list. Try again later.")]
    [InlineData(422, "EMAIL_REFUSED", "providerReason", "Email address is not verified.",
        "The email service refused this email: Email address is not verified. The failed try is now in the list.")]
    [InlineData(422, "RESEND_NOT_POSSIBLE", "failureReason", "missingRecipient", "This email cannot be resent: No email address.")]
    [InlineData(404, "NOT_FOUND", "detail", "No send log entry exists.", "We couldn't find that email. It may have been removed.")]
    [InlineData(401, "UNAUTHORIZED", "detail", "Send the RouteGo internal key.",
        "The email log is not set up correctly: the portal cannot connect to the Notification service. Tell the platform engineer.")]
    public async Task Post_ResendRefused_TellsWhyAndReloadsList(int status, string code, string extensionName, string extensionValue, string expected)
    {
        // Arrange
        var host = new EmailLogTestHost();
        var log = new OwnerSendLog();
        var extensions = new Dictionary<string, object> { [extensionName] = extensionValue };
        host.Notification.Map(HttpMethod.Get, EmailLogTestData.SearchPath, log.Search);
        host.Notification.Map(HttpMethod.Post, _resendPath, () => IdentityStubs.Problem((HttpStatusCode)status, code, extensions));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{EmailLogTestData.EmailLogPath}{SearchAddress}&resend={EmailLogTestData.FailedId}");
        int searchesBefore = SearchCount(host);

        // Act
        IDocument result = await ConfirmAndReadAsync(form, page);

        // Assert
        Assert.Equal(expected, result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal(searchesBefore + 1, SearchCount(host));
        Assert.NotNull(result.QuerySelector("[data-email-log-table]"));
        Assert.NotNull(result.QuerySelector("[data-signed-in-as]"));
    }

    [Fact]
    public async Task Post_NotificationUnreachable_ShowsUpstreamMessageWithoutRetrying()
    {
        // Arrange
        var host = new EmailLogTestHost();
        var log = new OwnerSendLog();
        host.Notification.Map(HttpMethod.Get, EmailLogTestData.SearchPath, log.Search);
        host.Notification.Map(HttpMethod.Post, _resendPath, () => throw new HttpRequestException("connection refused"));
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{EmailLogTestData.EmailLogPath}{SearchAddress}&resend={EmailLogTestData.FailedId}");

        // Act
        IDocument result = await ConfirmAndReadAsync(form, page);

        // Assert
        Assert.Equal(
            "This part of RouteGo is not available right now. Please try again in a few minutes.",
            result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Single(host.Notification.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Post_Resend_LogsTheAdminAndTheEntry()
    {
        // Arrange
        var host = new EmailLogTestHost();
        var log = new OwnerSendLog();
        host.Notification.Map(HttpMethod.Get, EmailLogTestData.SearchPath, log.Search);
        host.Notification.Map(HttpMethod.Post, _resendPath, log.Resend);
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{EmailLogTestData.EmailLogPath}{SearchAddress}&resend={EmailLogTestData.FailedId}");

        // Act
        await ConfirmAsync(form, page);

        // Assert
        Assert.Contains($"Admin {host.UserId} asked to resend send log entry {EmailLogTestData.FailedId}: sent", host.Log.Lines);
        Assert.DoesNotContain(host.Log.Lines, line => line.Contains(TestHost.TestInternalKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Get_Cancel_ReturnsToListWithoutResending()
    {
        // Arrange
        var host = new EmailLogTestHost();
        var log = new OwnerSendLog();
        host.Notification.Map(HttpMethod.Get, EmailLogTestData.SearchPath, log.Search);
        FormClient form = await host.SignInAsync(PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{EmailLogTestData.EmailLogPath}{SearchAddress}&resend={EmailLogTestData.FailedId}");
        string cancelHref = page.QuerySelector("[data-cancel]")!.GetAttribute("href")!;

        // Act
        IDocument result = await form.GetPageAsync(cancelHref);

        // Assert
        Assert.Equal("/admin/email-log?email=owner@busco.in&pageNumber=1", cancelHref);
        Assert.Null(result.QuerySelector("[data-confirm]"));
        Assert.DoesNotContain(host.Notification.Requests, request => request.Method == HttpMethod.Post);
    }

    private static int SearchCount(EmailLogTestHost host)
    {
        return host.Notification.Requests.Count(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == EmailLogTestData.SearchPath);
    }

    // Sends the open confirmation with its hidden entry field, as the browser would.
    private static async Task<HttpResponseMessage> ConfirmAsync(FormClient form, IDocument page)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.SendLogEntryId"] = page.QuerySelector(ConfirmForm + " input[name='Input.SendLogEntryId']")!.GetAttribute("value")!,
        };
        return await form.SendFormAsync(page, ConfirmForm, fields);
    }

    private static async Task<IDocument> ConfirmAndReadAsync(FormClient form, IDocument page)
    {
        HttpResponseMessage response = await ConfirmAsync(form, page);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await form.ReadDocumentAsync(response, page.Url);
    }

    /// <summary>Notification's send log for owner@busco.in: one failed email, and a resend adds a sent one first.</summary>
    private sealed class OwnerSendLog
    {
        private readonly List<object> _entries = new List<object>();

        public OwnerSendLog()
        {
            _entries.Add(EmailLogTestData.Entry(EmailLogTestData.FailedId, "failed", "refusedByEmailService", null));
        }

        public HttpResponseMessage Search()
        {
            return EmailLogTestData.Answer(_entries.Count, 1, _entries.ToArray());
        }

        public HttpResponseMessage Resend()
        {
            object resent = EmailLogTestData.Entry(EmailLogTestData.ResentId, "sent", null, EmailLogTestData.FailedId);
            _entries.Insert(0, resent);
            return FakeBackend.Json(resent, HttpStatusCode.OK);
        }
    }
}
