using System.Net;
using System.Net.Http.Headers;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Io;

namespace RouteGo.App.Tests.Testing;

/// <summary>
/// Drives the shared bus form the way a browser does: change fields on the drawn page, then press a
/// button. Every field the form holds is sent as it stands, with the pressed button's name and
/// value, to the button's own address — so builder steps and "Save bus" are tested end to end.
/// </summary>
public sealed class BusFormDriver
{
    private readonly HttpClient _client;
    private readonly FormClient _form;

    public BusFormDriver(HttpClient client, IDocument page)
    {
        _client = client;
        _form = new FormClient(client);
        Page = page;
    }

    /// <summary>The page as last drawn.</summary>
    public IDocument Page { get; private set; }

    /// <summary>The answer to the last button pressed.</summary>
    public HttpResponseMessage? LastResponse { get; private set; }

    /// <summary>Types into a box or picks in a choice, found by its field name.</summary>
    public void Set(string name, string value)
    {
        IElement field = Page.QuerySelector($"[name='{name}']") ?? throw new InvalidOperationException($"no field: {name}");
        if (field is IHtmlSelectElement choice)
        {
            choice.Value = value;
            return;
        }

        ((IHtmlInputElement)field).Value = value;
    }

    /// <summary>Ticks or unticks the box or radio button with the name and value.</summary>
    public void Tick(string name, string value, bool isTicked)
    {
        IElement field = Page.QuerySelector($"input[name='{name}'][value='{value}']") ?? throw new InvalidOperationException($"no box: {name}={value}");
        ((IHtmlInputElement)field).IsChecked = isTicked;
    }

    /// <summary>Presses a button and, when the page is drawn again (200), keeps the new page.</summary>
    public async Task<HttpResponseMessage> PressAsync(string buttonSelector)
    {
        IHtmlButtonElement button = Page.QuerySelector(buttonSelector) as IHtmlButtonElement ?? throw new InvalidOperationException($"no button: {buttonSelector}");
        var form = (IHtmlFormElement)button.Form!;
        DocumentRequest submission = form.GetSubmission(button)!;
        // GetSubmission builds the body a browser sends but always aims at the form's own action;
        // a browser sends it to the button's formaction when the button has one.
        string target = button.GetAttribute("formaction") ?? form.GetAttribute("action")!;
        string address = new Uri(_client.BaseAddress!, target).AbsoluteUri;

        var content = new StreamContent(submission.Body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        HttpResponseMessage response = await _client.PostAsync(address, content);
        LastResponse = response;
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Page = await _form.ReadDocumentAsync(response, address);
            return response;
        }

        int status = (int)response.StatusCode;
        bool isRedirect = status >= 300 && status < 400;
        if (!isRedirect)
        {
            string body = await response.Content.ReadAsStringAsync();
            string start = body.Length > 300 ? body.Substring(0, 300) : body;
            throw new InvalidOperationException($"{status} from {address}: {start}");
        }

        return response;
    }

    /// <summary>Names the seat classes, adding a class box for each name after the first.</summary>
    public async Task NameSeatClassesAsync(params string[] names)
    {
        Set("Input.SeatClasses[0]", names[0]);
        for (int index = 1; index < names.Length; index++)
        {
            await PressAsync("[data-add-seat-class]");
            Set($"Input.SeatClasses[{index}]", names[index]);
        }
    }

    /// <summary>Gives a deck its size and fills it with seats; ticks the upper deck first when needed.</summary>
    public async Task FillDeckAsync(string deck, int rows, int columns, string aisleAfterColumn)
    {
        string field = deck == "upper" ? "Input.Upper" : "Input.Lower";
        if (deck == "upper")
        {
            Tick("Input.HasUpperDeck", "true", true);
        }

        Set(field + ".Rows", rows.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set(field + ".Columns", columns.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set(field + ".AisleAfterColumn", aisleAfterColumn);
        await PressAsync($"[data-deck='{deck}'] [data-fill]");
    }

    /// <summary>
    /// The bus of the "save a new bus" example: two classes, a 6 by 3 lower deck in "Lower berth" and
    /// a 6 by 3 upper deck in "Upper berth" (36 seats), with every detail filled in.
    /// </summary>
    public async Task FillShivneriSleeperAsync()
    {
        await NameSeatClassesAsync("Lower berth", "Upper berth");
        await FillDeckAsync("lower", 6, 3, "1");
        await FillDeckAsync("upper", 6, 3, "1");
        Set("Input.Upper.ClassForAll", "Upper berth");
        await PressAsync("[data-deck='upper'] [data-deck-class-apply]");
        Set("Input.Name", "Shivneri Sleeper");
        Set("Input.RegistrationNumber", "MH-12 AB 1234");
        Tick("Input.BusType", "sleeper", true);
        Tick("Input.IsAc", "true", true);
        Tick("Input.Amenities", "blanket", true);
        Tick("Input.Amenities", "chargingPoint", true);
    }

    /// <summary>The seat numbers on a deck, row by row, as typed in their boxes.</summary>
    public string[] SeatLabels(string deck)
    {
        IHtmlCollection<IElement> boxes = Page.QuerySelectorAll($"[data-deck='{deck}'] [data-seat-label]");
        return boxes.Select(box => ((IHtmlInputElement)box).Value).ToArray();
    }

    /// <summary>The seat class chosen for every seat on a deck, row by row.</summary>
    public string[] SeatClasses(string deck)
    {
        IHtmlCollection<IElement> choices = Page.QuerySelectorAll($"[data-deck='{deck}'] [data-seat-class]");
        return choices.Select(choice => ((IHtmlSelectElement)choice).Value ?? string.Empty).ToArray();
    }

    /// <summary>The seat whose seat number box holds the label.</summary>
    public IElement Seat(string label)
    {
        return Page.QuerySelector($"[data-seat='{label}']") ?? throw new InvalidOperationException($"no seat: {label}");
    }

    /// <summary>The field name of a seat's part ("Label", "SeatClass"), for the seat now holding the label.</summary>
    public string SeatField(string label, string part)
    {
        string index = Seat(label).GetAttribute("data-seat-index")!;
        return $"Input.Seats[{index}].{part}";
    }

    /// <summary>The trimmed text of the first element the selector finds, or null.</summary>
    public string? Text(string selector)
    {
        IElement? element = Page.QuerySelector(selector);
        if (element is null)
        {
            return null;
        }

        return element.TextContent.Trim();
    }
}
