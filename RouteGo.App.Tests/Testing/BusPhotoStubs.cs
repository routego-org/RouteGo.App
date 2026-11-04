using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace RouteGo.App.Tests.Testing;

/// <summary>
/// Fleet's answers for the bus photos page — bus "Alpha" with photos named P1 to P5 — and the page
/// script's steps, posted the way the script posts them (the page's anti-forgery token, the
/// address the page drew).
/// </summary>
public static class BusPhotoStubs
{
    public static readonly Guid BusId = Guid.Parse("7a2b3c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d");
    public static readonly string PageAddress = $"/operator/buses/{BusId}/photos";
    public static readonly string BusPath = $"/buses/{BusId}";
    public static readonly string PhotosPath = $"/buses/{BusId}/photos";
    public static readonly string OrderPath = $"/buses/{BusId}/photoOrder";

    /// <summary>The photo named P1 to P5.</summary>
    public static Guid Photo(int number)
    {
        return Guid.Parse($"00000000-0000-4000-8000-00000000000{number}");
    }

    public static string PhotoPath(int number)
    {
        return $"{PhotosPath}/{Photo(number)}";
    }

    /// <summary>Where photo Pn is delivered from.</summary>
    public static string PhotoUrl(int number)
    {
        return $"https://photos.test/buses/{BusId}/p{number}.jpg";
    }

    /// <summary>Fleet's getBus answer for "Alpha" with the photos in the order given.</summary>
    public static HttpResponseMessage BusWithPhotos(params int[] photoNumbers)
    {
        var photos = new List<object>();
        for (int index = 0; index < photoNumbers.Length; index++)
        {
            photos.Add(PhotoBody(photoNumbers[index], index + 1));
        }

        return FakeBackend.Json(new
        {
            busId = BusId,
            operatorId = Guid.NewGuid(),
            registrationNumber = "MH12AB1234",
            name = "Alpha",
            busType = "seater",
            isAc = true,
            amenities = Array.Empty<string>(),
            seatClasses = new[] { "Seater" },
            layout = new { decks = Array.Empty<object>(), seats = Array.Empty<object>() },
            totalSeats = 0,
            status = "active",
            photos,
            createdAt = new DateTimeOffset(2026, 10, 1, 4, 30, 0, TimeSpan.Zero),
            updatedAt = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero),
        });
    }

    /// <summary>Fleet's answer to a new order: the photos in that order.</summary>
    public static HttpResponseMessage PhotoList(params int[] photoNumbers)
    {
        var items = new List<object>();
        for (int index = 0; index < photoNumbers.Length; index++)
        {
            items.Add(PhotoBody(photoNumbers[index], index + 1));
        }

        return FakeBackend.Json(new { items });
    }

    /// <summary>Fleet's upload slot for photo P9 with the address the browser sends it to.</summary>
    public static HttpResponseMessage Slot(string uploadUrl, string contentType)
    {
        var slot = new
        {
            photoId = Photo(9),
            uploadUrl,
            uploadMethod = "put",
            contentType,
            expiresAt = new DateTimeOffset(2026, 10, 9, 5, 35, 0, TimeSpan.Zero),
        };
        return FakeBackend.Json(slot, System.Net.HttpStatusCode.Created);
    }

    /// <summary>Fleet's confirmBusPhoto answer: photo P9 is uploaded.</summary>
    public static HttpResponseMessage Confirmed()
    {
        return FakeBackend.Json(PhotoBody(9, 3));
    }

    /// <summary>
    /// Posts one of the page script's steps to the address the page drew for it
    /// (data-slot-address, data-confirm-address...), with the page's anti-forgery token.
    /// </summary>
    public static async Task<HttpResponseMessage> PostStepAsync(
        HttpClient client, IDocument page, string addressAttribute, Dictionary<string, string> fields)
    {
        IElement form = page.QuerySelector("form[data-upload-form]")!;
        var token = (IHtmlInputElement)form.QuerySelector("input[name=__RequestVerificationToken]")!;
        var payload = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token.Value };
        string address = form.GetAttribute(addressAttribute)!;
        return await client.PostAsync(address, new FormUrlEncodedContent(payload));
    }

    private static object PhotoBody(int number, int position)
    {
        return new
        {
            photoId = Photo(number),
            status = "uploaded",
            format = "jpeg",
            sizeBytes = 1258291,
            position,
            url = PhotoUrl(number),
            createdAt = new DateTimeOffset(2026, 10, 2, 4, 30, 0, TimeSpan.Zero),
            uploadedAt = new DateTimeOffset(2026, 10, 2, 4, 31, 0, TimeSpan.Zero),
        };
    }
}
