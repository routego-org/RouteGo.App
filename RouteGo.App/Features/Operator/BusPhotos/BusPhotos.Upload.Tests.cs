using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AngleSharp.Dom;
using static RouteGo.App.Tests.Testing.BusPhotoStubs;

namespace RouteGo.App.Features.Operator.BusPhotos;

public sealed class BusPhotosUploadTests
{
    private const string SlotAddress = "data-slot-address";
    private const string ConfirmAddress = "data-confirm-address";
    private const string DiscardAddress = "data-discard-address";

    private static readonly string _confirmPath = PhotoPath(9) + "/confirm";
    private static readonly string _uploadUrl = $"https://uploads.test/buses/{BusId}/p9.jpg?signature=abc";

    [Fact]
    public async Task PostSlot_GoodFile_AsksFleetWithFormatAndSizeAndAnswersWhereToSendIt()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        host.Fleet.Map(HttpMethod.Post, PhotosPath, () => Slot(_uploadUrl, "image/jpeg"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);

        // Act
        HttpResponseMessage response = await PostStepAsync(client, page, SlotAddress, File("front.jpg", "jpeg", "1258291"));
        JsonElement answer = await AnswerAsync(response);

        // Assert
        Assert.Equal("{\"format\":\"jpeg\",\"sizeBytes\":1258291}", host.Fleet.LastRequestBody);
        Assert.Equal("Bearer " + host.AccessToken, host.Fleet.LastRequestHeader("Authorization"));
        Assert.Equal("send", answer.GetProperty("outcome").GetString());
        Assert.Equal(Photo(9).ToString(), answer.GetProperty("photoId").GetString());
        Assert.Equal(_uploadUrl, answer.GetProperty("uploadUrl").GetString());
        Assert.Equal("PUT", answer.GetProperty("uploadMethod").GetString());
        Assert.Equal("image/jpeg", answer.GetProperty("contentType").GetString());
    }

    [Fact]
    public async Task PostConfirm_FileArrived_AnswersAdded()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        host.Fleet.Map(HttpMethod.Post, _confirmPath, Confirmed);
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);

        // Act
        HttpResponseMessage response = await PostStepAsync(client, page, ConfirmAddress, Confirm("front.jpg", 1));
        JsonElement answer = await AnswerAsync(response);

        // Assert
        Assert.Equal(1, host.FleetCalls(HttpMethod.Post, _confirmPath));
        Assert.Equal("added", answer.GetProperty("outcome").GetString());
        Assert.Equal("front.jpg — added", answer.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PostDiscard_FileCouldNotBeSent_DeletesThePendingPhotoAndSaysNotSent()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        host.Fleet.Map(HttpMethod.Delete, PhotoPath(9), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);
        var fields = new Dictionary<string, string> { ["photoId"] = Photo(9).ToString(), ["fileName"] = "b.jpg" };

        // Act
        HttpResponseMessage response = await PostStepAsync(client, page, DiscardAddress, fields);
        JsonElement answer = await AnswerAsync(response);

        // Assert
        Assert.Equal(1, host.FleetCalls(HttpMethod.Delete, PhotoPath(9)));
        Assert.Equal("b.jpg — not added: the file could not be sent. Check your connection and try again.", answer.GetProperty("message").GetString());
        Assert.False(answer.GetProperty("stops").GetBoolean());
    }

    [Theory]
    [InlineData("formatMismatch", "c.jpg — not added: the file is not the type its name says. Save it again as JPEG or PNG and try again.")]
    [InlineData("tooLarge", "c.jpg — not added: the file is larger than 3 MB.")]
    [InlineData("unsupportedFormat", "c.jpg — not added: only JPEG and PNG photos can be used.")]
    public async Task PostConfirm_FleetRefusesTheStoredFile_SaysWhy(string reason, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        var extensions = new Dictionary<string, object> { ["reason"] = reason };
        host.Fleet.Map(HttpMethod.Post, _confirmPath, () => IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "PHOTO_INVALID", extensions));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);

        // Act
        HttpResponseMessage response = await PostStepAsync(client, page, ConfirmAddress, Confirm("c.jpg", 1));
        JsonElement answer = await AnswerAsync(response);

        // Assert
        Assert.Equal("failed", answer.GetProperty("outcome").GetString());
        Assert.Equal(expected, answer.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PostConfirm_SlotExpired_SaysItTookTooLong()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        host.Fleet.Map(HttpMethod.Post, _confirmPath, () => IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "PHOTO_UPLOAD_EXPIRED"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);

        // Act
        HttpResponseMessage response = await PostStepAsync(client, page, ConfirmAddress, Confirm("a.jpg", 1));
        JsonElement answer = await AnswerAsync(response);

        // Assert
        Assert.Equal("a.jpg — not added: sending the photo took too long. Please try again.", answer.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PostConfirm_FileNotArrivedFirstTime_AsksTheScriptToAskAgain()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        host.Fleet.Map(HttpMethod.Post, _confirmPath, () => IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "PHOTO_NOT_UPLOADED"));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);

        // Act
        HttpResponseMessage response = await PostStepAsync(client, page, ConfirmAddress, Confirm("a.jpg", 1));
        JsonElement answer = await AnswerAsync(response);

        // Assert
        Assert.Equal("askAgain", answer.GetProperty("outcome").GetString());
        Assert.Equal(0, host.FleetCalls(HttpMethod.Delete, PhotoPath(9)));
    }

    [Fact]
    public async Task PostConfirm_FileNotArrivedSecondTime_DeletesThePendingPhotoAndSaysNotSent()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        host.Fleet.Map(HttpMethod.Post, _confirmPath, () => IdentityStubs.Problem(HttpStatusCode.UnprocessableEntity, "PHOTO_NOT_UPLOADED"));
        host.Fleet.Map(HttpMethod.Delete, PhotoPath(9), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);

        // Act
        HttpResponseMessage response = await PostStepAsync(client, page, ConfirmAddress, Confirm("a.jpg", 2));
        JsonElement answer = await AnswerAsync(response);

        // Assert
        Assert.Equal(1, host.FleetCalls(HttpMethod.Delete, PhotoPath(9)));
        Assert.Equal("a.jpg — not added: the file could not be sent. Check your connection and try again.", answer.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "PHOTO_LIMIT_REACHED", "front.jpg — not added: this bus already has 5 photos, or other photos are still being added. Wait 5 minutes or delete a photo.", false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "UPSTREAM_UNAVAILABLE", "front.jpg — not added: this part of RouteGo is not available right now. Please try again in a few minutes.", false)]
    [InlineData(HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "front.jpg — not added: something went wrong on our side.", false)]
    [InlineData(HttpStatusCode.Forbidden, "OPERATOR_PROFILE_NOT_READY", "Your operator account is being set up. Please try again in a minute.", true)]
    [InlineData(HttpStatusCode.Forbidden, "FORBIDDEN", "You don't have access to this.", true)]
    [InlineData(HttpStatusCode.NotFound, "NOT_FOUND", "We couldn't find that bus. It may have been removed.", true)]
    public async Task PostSlot_FleetRefuses_SaysWhyAndWhetherToStop(HttpStatusCode status, string code, string expected, bool stops)
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        host.Fleet.Map(HttpMethod.Post, PhotosPath, () => IdentityStubs.Problem(status, code));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);

        // Act
        HttpResponseMessage response = await PostStepAsync(client, page, SlotAddress, File("front.jpg", "jpeg", "1258291"));
        JsonElement answer = await AnswerAsync(response);

        // Assert
        Assert.Equal("failed", answer.GetProperty("outcome").GetString());
        Assert.Equal(expected, answer.GetProperty("message").GetString());
        Assert.Equal(stops, answer.GetProperty("stops").GetBoolean());
    }

    [Fact]
    public async Task PostSlot_FleetRefusesTheFileDetails_SaysOnlyJpegAndPngUpTo3Mb()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        host.Fleet.Map(HttpMethod.Post, PhotosPath, () => FakeBackend.ValidationError("sizeBytes", "At most 3145728."));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);

        // Act
        HttpResponseMessage response = await PostStepAsync(client, page, SlotAddress, File("front.jpg", "jpeg", "9999999"));
        JsonElement answer = await AnswerAsync(response);

        // Assert
        Assert.Equal("front.jpg — not added: only JPEG and PNG photos up to 3 MB can be used.", answer.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PostSlot_UploadAddressNotAWebAddress_FreesThePlaceAndSendsNothing()
    {
        // Arrange
        using var host = new FleetTestHost();
        host.Fleet.Map(HttpMethod.Get, BusPath, () => BusWithPhotos(1, 2));
        host.Fleet.Map(HttpMethod.Post, PhotosPath, () => Slot("javascript:alert(1)", "image/jpeg"));
        host.Fleet.Map(HttpMethod.Delete, PhotoPath(9), () => new HttpResponseMessage(HttpStatusCode.NoContent));
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);

        // Act
        HttpResponseMessage response = await PostStepAsync(client, page, SlotAddress, File("front.jpg", "jpeg", "1258291"));
        JsonElement answer = await AnswerAsync(response);

        // Assert
        Assert.Equal("failed", answer.GetProperty("outcome").GetString());
        Assert.False(answer.TryGetProperty("uploadUrl", out JsonElement address) && address.ValueKind == JsonValueKind.String);
        Assert.Equal(1, host.FleetCalls(HttpMethod.Delete, PhotoPath(9)));
    }

    private static Dictionary<string, string> File(string fileName, string format, string sizeBytes)
    {
        return new Dictionary<string, string> { ["fileName"] = fileName, ["format"] = format, ["sizeBytes"] = sizeBytes };
    }

    private static Dictionary<string, string> Confirm(string fileName, int attempt)
    {
        return new Dictionary<string, string>
        {
            ["photoId"] = Photo(9).ToString(),
            ["fileName"] = fileName,
            ["attempt"] = attempt.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private static async Task<JsonElement> AnswerAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
