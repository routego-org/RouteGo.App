using System.Net;
using AngleSharp.Dom;
using static RouteGo.App.Tests.Testing.BusPhotoStubs;

namespace RouteGo.App.Features.Operator.BusPhotos;

public sealed class BusPhotosTests
{
    private const string ConfirmForm = "form[data-confirm-form]";

    [Fact]
    public async Task Get_TwoPhotos_ShowsThemInOrderWithTheCoverFirst()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(3, 1));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("Photos of Alpha", page.QuerySelector("[data-page-title]")!.TextContent);
        Assert.Equal("2 of 5 photos", Text(page, "[data-photo-count]"));
        Assert.Equal(new[] { Photo(3).ToString(), Photo(1).ToString() }, PhotoIds(page));
        Assert.Equal("Cover photo", Text(page, $"[data-photo='{Photo(3)}'] [data-cover]"));
        Assert.Null(page.QuerySelector($"[data-photo='{Photo(1)}'] [data-cover]"));
        Assert.Equal(PhotoUrl(3), page.QuerySelector($"[data-photo='{Photo(3)}'] img")!.GetAttribute("src"));
        Assert.Equal(new[] { "Move later" }, Texts(page, $"[data-photo='{Photo(3)}'] button"));
        Assert.Equal(new[] { "Move earlier", "Make cover photo" }, Texts(page, $"[data-photo='{Photo(1)}'] button"));
        Assert.Equal("Delete", Text(page, $"[data-photo='{Photo(1)}'] [data-delete]"));
        Assert.Equal($"/operator/buses/{BusId}", page.QuerySelector("[data-back]")!.GetAttribute("href"));
        Assert.Equal("Back to Alpha", Text(page, "[data-back]"));
        Assert.Equal("Bearer " + host.AccessToken, host.Fleet.LastRequestHeader("Authorization"));
    }

    [Fact]
    public async Task Get_FewerThanFivePhotos_OffersAddPhotosThroughThePageScript()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        IElement upload = page.QuerySelector("form[data-upload-form]")!;
        Assert.True(upload.HasAttribute("hidden"));
        Assert.Equal("3", upload.GetAttribute("data-free"));
        Assert.Equal("3145728", upload.GetAttribute("data-max-bytes"));
        Assert.Equal("Add photos", Text(page, "[data-add-photos]"));
        Assert.Equal("JPEG or PNG, up to 3 MB each. The first photo is the one customers see first.", Text(page, "[data-photo-hint]"));
        Assert.Equal("image/jpeg,image/png", page.QuerySelector("[data-photo-files]")!.GetAttribute("accept"));
        Assert.NotNull(page.QuerySelector("script[src^='/js/bus-photos.js']"));
    }

    [Fact]
    public async Task Get_Page_DrawsTheWordsThePageScriptShows()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2, 3, 4));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        IElement upload = page.QuerySelector("form[data-upload-form]")!;
        Assert.Equal("Waiting", upload.GetAttribute("data-word-waiting"));
        Assert.Equal("Sending…", upload.GetAttribute("data-word-sending"));
        Assert.Equal("You can add {free} more photo. You chose {n}. Choose fewer files.", upload.GetAttribute("data-word-too-many-one"));
        Assert.Equal("You can add {free} more photos. You chose {n}. Choose fewer files.", upload.GetAttribute("data-word-too-many"));
        Assert.Equal("{file_name} — not added: the file is larger than 3 MB.", upload.GetAttribute("data-word-too-large"));
        Assert.Equal("{file_name} — not added: only JPEG and PNG photos can be used.", upload.GetAttribute("data-word-wrong-type"));
        Assert.Equal("{file_name} — not added: the file could not be sent. Check your connection and try again.", upload.GetAttribute("data-word-not-sent"));
        Assert.Equal("{file_name} — not added: something went wrong on our side.", upload.GetAttribute("data-word-server-error"));
    }

    [Fact]
    public async Task Get_NoPhotos_ShowsNoPhotosYetAndAddPhotos()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos());
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("No photos yet. Customers trust a bus more when they can see it.", Text(page, "[data-no-photos]"));
        Assert.Null(page.QuerySelector("[data-photo-count]"));
        Assert.Equal("5", page.QuerySelector("form[data-upload-form]")!.GetAttribute("data-free"));
    }

    [Fact]
    public async Task Get_FivePhotos_ShowsFullAndDoesNotOfferAddPhotos()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2, 3, 4, 5));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.Equal("This bus has the most photos it can have (5). Delete one to add another.", Text(page, "[data-photos-full]"));
        Assert.Null(page.QuerySelector("form[data-upload-form]"));
    }

    [Fact]
    public async Task Get_Section_DrawsThePhotoSectionAloneFromAFreshRead()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2, 9));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument section = await form.GetPageAsync(PageAddress + "?handler=Section");

        // Assert
        Assert.Equal("3 of 5 photos", Text(section, "[data-photo-section] [data-photo-count]"));
        Assert.Null(section.QuerySelector("[data-page-title]"));
        Assert.Null(section.QuerySelector("nav"));
    }

    [Fact]
    public async Task Get_Delete_AsksFirstAndSendsNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2, 3));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument page = await form.GetPageAsync(PageAddress);
        string questionAddress = page.QuerySelector($"[data-photo='{Photo(2)}'] [data-delete]")!.GetAttribute("href")!;

        // Act
        IDocument question = await form.GetPageAsync(questionAddress);

        // Assert
        Assert.Equal("Delete this photo? Customers will no longer see it.", Text(question, "[data-confirm-question]"));
        Assert.Equal("Delete photo", Text(question, ConfirmForm + " button"));
        Assert.Equal(PageAddress, question.QuerySelector("[data-cancel]")!.GetAttribute("href"));
        Assert.Null(question.QuerySelector("form[data-order-form]"));
        Assert.Null(question.QuerySelector("form[data-upload-form]"));
        Assert.Equal(0, host.FleetCalls(HttpMethod.Delete, PhotoPath(2)));
    }

    [Fact]
    public async Task Post_Delete_DeletesThePhotoAndSaysDeleted()
    {
        // Arrange
        using var host = new FleetTestHost();
        int[] photos = new[] { 1, 2, 3 };
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(photos));
        host.Fleet.Map(HttpMethod.Delete, PhotoPath(2), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument question = await form.GetPageAsync(PageAddress + $"?delete={Photo(2)}");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(question, ConfirmForm, new Dictionary<string, string> { ["photoId"] = Photo(2).ToString() });
        photos = new[] { 1, 3 };
        IDocument result = await form.GetPageAsync(response.Headers.Location!.OriginalString);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, host.FleetCalls(HttpMethod.Delete, PhotoPath(2)));
        Assert.Equal("Photo deleted.", Text(result, "[data-flash]"));
        Assert.Equal(new[] { Photo(1).ToString(), Photo(3).ToString() }, PhotoIds(result));
    }

    [Fact]
    public async Task Post_DeletePhotoAlreadyGone_SaysChangedAndReadsThemAgain()
    {
        // Arrange
        using var host = new FleetTestHost();
        int[] photos = new[] { 1, 2 };
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(photos));
        host.Fleet.Map(HttpMethod.Delete, PhotoPath(2), () => IdentityStubs.Problem(HttpStatusCode.NotFound, "NOT_FOUND"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument question = await form.GetPageAsync(PageAddress + $"?delete={Photo(2)}");
        photos = new[] { 1 };

        // Act
        HttpResponseMessage response = await form.SendFormAsync(question, ConfirmForm, new Dictionary<string, string> { ["photoId"] = Photo(2).ToString() });
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("The photos changed since you opened this page. We have loaded them again.", Text(result, "[data-page-alert]"));
        Assert.Equal(new[] { Photo(1).ToString() }, PhotoIds(result));
        Assert.Null(result.QuerySelector("[data-confirm]"));
    }

    [Fact]
    public async Task Post_DeleteFleetUnreachable_KeepsTheQuestionOpen()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        host.Fleet.Map(HttpMethod.Delete, PhotoPath(2), () => throw new HttpRequestException("connection refused"));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);
        IDocument question = await form.GetPageAsync(PageAddress + $"?delete={Photo(2)}");

        // Act
        HttpResponseMessage response = await form.SendFormAsync(question, ConfirmForm, new Dictionary<string, string> { ["photoId"] = Photo(2).ToString() });
        IDocument result = await form.ReadDocumentAsync(response);

        // Assert
        Assert.Equal(Messages.ProblemUpstream, Text(result, "[data-page-alert]"));
        Assert.NotNull(result.QuerySelector("[data-confirm]"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "NOT_FOUND", "We couldn't find that bus. It may have been removed.")]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "You don't have access to this.")]
    [InlineData(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY", "Your operator account is being set up. Please try again in a minute.")]
    public async Task Get_FleetRefuses_ShowsMessageInsteadOfPhotos(HttpStatusCode status, string code, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => IdentityStubs.Problem(status, code));
        FormClient form = await host.SignInAsync(PortalSession.RoleOperator);

        // Act
        IDocument page = await form.GetPageAsync(PageAddress);

        // Assert
        Assert.StartsWith(expected, Text(page, "[data-page-alert]"));
        Assert.Null(page.QuerySelector("[data-page-title]"));
        Assert.Null(page.QuerySelector("[data-section-title]"));
    }

    [Fact]
    public async Task Get_FleetAnswersUnauthorized_EndsSession()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => IdentityStubs.Problem(HttpStatusCode.Unauthorized, "UNAUTHORIZED"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);

        // Act
        HttpResponseMessage response = await client.GetAsync(PageAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("reason=session-ended", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Admin")]
    public async Task Get_NotAnOperator_ShowsNoPermissionWithoutCallingFleet(string role)
    {
        // Arrange
        using var host = new FleetTestHost();
        HttpClient client = await host.SignInClientAsync(role);

        // Act
        HttpResponseMessage response = await client.GetAsync(PageAddress);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Fleet.Requests);
    }

    private static string Text(IDocument page, string selector)
    {
        return page.QuerySelector(selector)!.TextContent.Trim();
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }

    private static string[] PhotoIds(IDocument page)
    {
        return page.QuerySelectorAll("[data-photos] [data-photo]").Select(photo => photo.GetAttribute("data-photo")!).ToArray();
    }
}
