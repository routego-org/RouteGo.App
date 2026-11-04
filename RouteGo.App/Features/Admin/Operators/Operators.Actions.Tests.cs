using System.Net;
using AngleSharp.Dom;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RouteGo.App.Features.Admin.Operators;

public sealed class OperatorsActionsTests
{
    private const string ConfirmForm = "form[data-confirm-form]";
    private const string Reason = "GSTIN does not match company name";

    private static readonly string _approvePath = $"{OperatorsTestData.ListPath}/{OperatorsTestData.BusCoId}/approve";
    private static readonly string _rejectPath = $"{OperatorsTestData.ListPath}/{OperatorsTestData.BusCoId}/reject";

    [Fact]
    public async Task Post_Approve_ApprovesAndReloadsList()
    {
        // Arrange
        var fake = new FakeBackend();
        var busCo = new BusCoApplication("pendingApproval");
        fake.Map(HttpMethod.Get, OperatorsTestData.ListPath, busCo.List);
        fake.Map(HttpMethod.Post, _approvePath, () => busCo.Decide("approved"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await OperatorsTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{OperatorsTestData.OperatorsPath}?approve={OperatorsTestData.BusCoId}");
        int listsBefore = OperatorsTestData.ListCount(fake);

        // Act
        HttpResponseMessage response = await ConfirmAsync(form, page, "");
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(
            "Approve BusCo Travels? They will be able to sign in and sell bus tickets. We will email them.",
            page.QuerySelector("[data-confirm-question]")!.TextContent);
        Assert.Equal("Approve", page.QuerySelector(ConfirmForm + " button")!.TextContent);
        Assert.Equal("Cancel", page.QuerySelector("[data-cancel]")!.TextContent);
        Assert.Null(page.QuerySelector(ConfirmForm + " textarea"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/operators?pageNumber=1", response.Headers.Location!.OriginalString);
        Assert.Contains(fake.Requests, request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == _approvePath);
        Assert.Equal("BusCo Travels is approved. We will email them, and they can sign in now.", result.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal(listsBefore + 1, OperatorsTestData.ListCount(fake));
    }

    [Fact]
    public async Task Post_RejectWithReason_RejectsAndReloadsList()
    {
        // Arrange
        var fake = new FakeBackend();
        var busCo = new BusCoApplication("pendingApproval");
        fake.Map(HttpMethod.Get, OperatorsTestData.ListPath, busCo.List);
        fake.Map(HttpMethod.Post, _rejectPath, () => busCo.Decide("rejected"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await OperatorsTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{OperatorsTestData.OperatorsPath}?reject={OperatorsTestData.BusCoId}");

        // Act
        HttpResponseMessage response = await ConfirmAsync(form, page, Reason);
        string? sentBody = fake.LastRequestBody;
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(
            "Reject BusCo Travels? We will email them the reason you write here. They can re-apply after 7 days.",
            page.QuerySelector("[data-confirm-question]")!.TextContent);
        Assert.Equal("Reason", page.QuerySelector(ConfirmForm + " label")!.TextContent);
        Assert.Equal("Reject application", page.QuerySelector(ConfirmForm + " button")!.TextContent);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("{\"reason\":\"GSTIN does not match company name\"}", sentBody);
        Assert.Equal(
            "BusCo Travels is rejected. We will email them the reason. They can re-apply after 7 days.",
            result.QuerySelector("[data-flash]")!.TextContent);
    }

    [Fact]
    public async Task Post_ReasonTooShort_ShowsMessageWithoutCallingIdentity()
    {
        // Arrange
        var fake = new FakeBackend();
        var busCo = new BusCoApplication("pendingApproval");
        fake.Map(HttpMethod.Get, OperatorsTestData.ListPath, busCo.List);
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await OperatorsTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{OperatorsTestData.OperatorsPath}?reject={OperatorsTestData.BusCoId}");

        // Act
        IDocument result = await ConfirmAndReadAsync(form, page, "bad");

        // Assert
        Assert.Equal("Reason must be between 10 and 500 characters.", result.QuerySelector("[data-valmsg-for='Input.Reason']")!.TextContent.Trim());
        Assert.Equal("bad", result.QuerySelector("textarea[name='Input.Reason']")!.TextContent.Trim());
        Assert.NotNull(result.QuerySelector("[data-confirm]"));
        Assert.DoesNotContain(fake.Requests, request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == _rejectPath);
    }

    [Theory]
    [InlineData("reject", "APPLICATION_NOT_PENDING", "approved", "This application is not waiting for review any more. It is now Approved. The list has been updated.")]
    [InlineData("approve", "APPLICATION_NOT_PENDING", "rejected", "This application is not waiting for review any more. It is now Rejected. The list has been updated.")]
    [InlineData("approve", "NOT_FOUND", "", "We couldn't find that operator. It may have been removed.")]
    public async Task Post_DecidedMeanwhile_TellsWhyAndReloadsList(string decision, string code, string currentStatus, string expected)
    {
        // Arrange
        var fake = new FakeBackend();
        var busCo = new BusCoApplication("pendingApproval");
        var extensions = new Dictionary<string, object> { ["currentStatus"] = currentStatus };
        fake.Map(HttpMethod.Get, OperatorsTestData.ListPath, busCo.List);
        fake.Map(HttpMethod.Post, $"{OperatorsTestData.ListPath}/{OperatorsTestData.BusCoId}/{decision}", () => IdentityStubs.Problem(HttpStatusCode.Conflict, code, extensions));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await OperatorsTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{OperatorsTestData.OperatorsPath}?{decision}={OperatorsTestData.BusCoId}");
        int listsBefore = OperatorsTestData.ListCount(fake);

        // Act
        HttpResponseMessage response = await ConfirmAsync(form, page, Reason);
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, result.QuerySelector("[data-flash]")!.TextContent);
        Assert.Equal(listsBefore + 1, OperatorsTestData.ListCount(fake));
    }

    [Fact]
    public async Task Post_IdentityUnreachable_KeepsConfirmationAndReason()
    {
        // Arrange
        var fake = new FakeBackend();
        var busCo = new BusCoApplication("pendingApproval");
        fake.Map(HttpMethod.Get, OperatorsTestData.ListPath, busCo.List);
        fake.Map(HttpMethod.Post, _rejectPath, () => throw new HttpRequestException("connection refused"));
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await OperatorsTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{OperatorsTestData.OperatorsPath}?reject={OperatorsTestData.BusCoId}");

        // Act
        IDocument result = await ConfirmAndReadAsync(form, page, Reason);

        // Assert
        Assert.Equal(
            "This part of RouteGo is not available right now. Please try again in a few minutes.",
            result.QuerySelector("[data-page-alert]")!.TextContent.Trim());
        Assert.Equal(Reason, result.QuerySelector("textarea[name='Input.Reason']")!.TextContent.Trim());
        Assert.NotNull(result.QuerySelector("[data-confirm]"));
    }

    [Fact]
    public async Task Get_Cancel_ReturnsToListWithoutDeciding()
    {
        // Arrange
        var fake = new FakeBackend();
        var busCo = new BusCoApplication("pendingApproval");
        fake.Map(HttpMethod.Get, OperatorsTestData.ListPath, busCo.List);
        using WebApplicationFactory<Program> factory = TestSignIn.CreateFactory(fake);
        FormClient form = await OperatorsTestData.SignInAsync(factory, fake, PortalSession.RoleAdmin);
        IDocument page = await form.GetPageAsync($"{OperatorsTestData.OperatorsPath}?status=pendingApproval&approve={OperatorsTestData.BusCoId}");
        string cancelHref = page.QuerySelector("[data-cancel]")!.GetAttribute("href")!;

        // Act
        IDocument result = await form.GetPageAsync(cancelHref);

        // Assert
        Assert.Equal("/admin/operators?status=pendingApproval&pageNumber=1", cancelHref);
        Assert.Null(result.QuerySelector("[data-confirm]"));
        Assert.DoesNotContain(fake.Requests, request => request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.StartsWith(OperatorsTestData.ListPath, StringComparison.Ordinal));
    }

    // Sends the open confirmation with its hidden operator fields, as the browser would.
    private static async Task<HttpResponseMessage> ConfirmAsync(FormClient form, IDocument page, string reason)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.OperatorId"] = page.QuerySelector(ConfirmForm + " input[name='Input.OperatorId']")!.GetAttribute("value")!,
            ["Input.CompanyName"] = page.QuerySelector(ConfirmForm + " input[name='Input.CompanyName']")!.GetAttribute("value")!,
            ["Input.Reason"] = reason,
        };
        return await form.SendFormAsync(page, ConfirmForm, fields);
    }

    private static async Task<IDocument> ConfirmAndReadAsync(FormClient form, IDocument page, string reason)
    {
        HttpResponseMessage response = await ConfirmAsync(form, page, reason);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await form.ReadDocumentAsync(response, page.Url);
    }

    /// <summary>Identity's copy of BusCo's application: the list shows it, and a decision updates its status.</summary>
    private sealed class BusCoApplication
    {
        private string _status;

        public BusCoApplication(string status)
        {
            _status = status;
        }

        public HttpResponseMessage List()
        {
            return OperatorsTestData.Answer(1, 1, OperatorsTestData.Application(_status));
        }

        public HttpResponseMessage Decide(string status)
        {
            _status = status;
            return FakeBackend.Json(OperatorsTestData.Application(status), HttpStatusCode.OK);
        }
    }
}
