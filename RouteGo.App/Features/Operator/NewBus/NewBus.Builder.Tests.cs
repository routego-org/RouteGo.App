using AngleSharp.Dom;

namespace RouteGo.App.Features.Operator.NewBus;

public sealed class NewBusBuilderTests
{
    private const string PageAddress = "/operator/buses/new";

    private static readonly string[] _lowerSixByThree = new[]
    {
        "1A", "1B", "1C", "2A", "2B", "2C", "3A", "3B", "3C", "4A", "4B", "4C", "5A", "5B", "5C", "6A", "6B", "6C",
    };

    [Fact]
    public async Task PostBuild_FillLowerDeck_NamesSeatsRowByRowWithFirstClass()
    {
        // Arrange
        using var host = new FleetTestHost();
        BusFormDriver form = await OpenAsync(host);
        await form.NameSeatClassesAsync("Lower berth", "Upper berth");

        // Act
        await form.FillDeckAsync("lower", 6, 3, "1");

        // Assert
        Assert.Equal(_lowerSixByThree, form.SeatLabels("lower"));
        Assert.All(form.SeatClasses("lower"), seatClass => Assert.Equal("Lower berth", seatClass));
        Assert.Equal("Seats: 18 of 60", form.Text("[data-seat-count]"));
        Assert.Equal(new[] { "Lower berth: 18 seats", "Upper berth: 0 seats" }, Texts(form.Page, "[data-class-count]"));
        Assert.Equal(4, form.Page.QuerySelectorAll("[data-deck='lower'] [data-seat-row]")[0].Children.Length);
        Assert.NotNull(form.Page.QuerySelectorAll("[data-deck='lower'] [data-seat-row]")[0].Children[1].GetAttribute("data-aisle"));
        Assert.Empty(host.Fleet.Requests);
    }

    [Fact]
    public async Task PostBuild_FillUpperDeckThenWholeDeckClass_NamesWithUAndSetsClass()
    {
        // Arrange
        using var host = new FleetTestHost();
        BusFormDriver form = await OpenAsync(host);
        await form.NameSeatClassesAsync("Lower berth", "Upper berth");
        await form.FillDeckAsync("upper", 6, 3, "1");
        string[] filledClasses = form.SeatClasses("upper");
        form.Set("Input.Upper.ClassForAll", "Upper berth");

        // Act
        await form.PressAsync("[data-deck='upper'] [data-deck-class-apply]");

        // Assert
        Assert.Equal("U1A", form.SeatLabels("upper")[0]);
        Assert.Equal("U6C", form.SeatLabels("upper")[17]);
        Assert.All(filledClasses, seatClass => Assert.Equal("Lower berth", seatClass));
        Assert.All(form.SeatClasses("upper"), seatClass => Assert.Equal("Upper berth", seatClass));
        Assert.True(((AngleSharp.Html.Dom.IHtmlInputElement)form.Page.QuerySelector("[data-has-upper-deck]")!).IsChecked);
    }

    [Fact]
    public async Task PostBuild_ChangeSingleSeats_RemovesRenamesReclassesAndSuggestsName()
    {
        // Arrange
        using var host = new FleetTestHost();
        BusFormDriver form = await OpenAsync(host);
        await form.NameSeatClassesAsync("Lower berth", "Upper berth");
        await form.FillDeckAsync("lower", 6, 3, "1");
        await form.PressAsync("[data-seat='6C'] [data-remove-seat]");
        int seatsAfterRemove = form.SeatLabels("lower").Length;
        bool isPlaceEmpty = form.Page.QuerySelectorAll("[data-deck='lower'] [data-seat-row]")[5].QuerySelector("[data-place-empty]") != null;
        form.Set(form.SeatField("1A", "Label"), "L1");
        form.Set(form.SeatField("2B", "SeatClass"), "Upper berth");

        // Act
        await form.PressAsync("[data-deck='lower'] [data-add-seat]");

        // Assert
        Assert.Equal(17, seatsAfterRemove);
        Assert.True(isPlaceEmpty);
        Assert.Equal("L1", form.SeatLabels("lower")[0]);
        Assert.Equal("Upper berth", form.SeatClasses("lower")[4]);
        Assert.Equal("6C", form.SeatLabels("lower")[17]);
        Assert.Equal("Lower berth", form.SeatClasses("lower")[17]);
        Assert.Equal("Seats: 18 of 60", form.Text("[data-seat-count]"));
    }

    [Fact]
    public async Task PostBuild_FillDeckThatHasSeats_AsksFirstThenReplaces()
    {
        // Arrange
        using var host = new FleetTestHost();
        BusFormDriver form = await OpenAsync(host);
        await form.NameSeatClassesAsync("Lower berth");
        await form.FillDeckAsync("lower", 6, 3, "1");
        form.Set(form.SeatField("1A", "Label"), "L1");

        // Act
        await form.PressAsync("[data-deck='lower'] [data-fill]");
        string? question = form.Text("[data-question-text]");
        string? confirmText = form.Text("[data-question-confirm]");
        string labelWhileAsking = form.SeatLabels("lower")[0];
        await form.PressAsync("[data-question-confirm]");

        // Assert
        Assert.Equal("Replace all seats on the lower deck? Your changes to these seats will be lost.", question);
        Assert.Equal("Replace seats", confirmText);
        Assert.Equal("L1", labelWhileAsking);
        Assert.Equal("1A", form.SeatLabels("lower")[0]);
        Assert.Null(form.Page.QuerySelector("[data-builder-question]"));
    }

    [Fact]
    public async Task PostBuild_CancelRefill_KeepsChangedSeats()
    {
        // Arrange
        using var host = new FleetTestHost();
        BusFormDriver form = await OpenAsync(host);
        await form.NameSeatClassesAsync("Lower berth");
        await form.FillDeckAsync("lower", 2, 2, "");
        form.Set(form.SeatField("1A", "Label"), "L1");
        await form.PressAsync("[data-deck='lower'] [data-fill]");

        // Act
        await form.PressAsync("[data-question-cancel]");

        // Assert
        Assert.Equal(new[] { "L1", "1B", "2A", "2B" }, form.SeatLabels("lower"));
        Assert.Null(form.Page.QuerySelector("[data-builder-question]"));
    }

    [Fact]
    public async Task PostSave_DeckMadeSmaller_RemovesSeatsOutsideAndSaysHowMany()
    {
        // Arrange
        using var host = new FleetTestHost();
        BusFormDriver form = await OpenAsync(host);
        await form.NameSeatClassesAsync("Lower berth");
        await form.FillDeckAsync("lower", 6, 3, "1");
        form.Set("Input.Lower.Rows", "5");

        // Act
        await form.PressAsync("[data-save]");

        // Assert
        Assert.Equal("3 seats outside the new size were removed.", form.Text("[data-builder-notice]"));
        Assert.Equal(15, form.SeatLabels("lower").Length);
        Assert.Empty(host.Fleet.Requests);
    }

    [Fact]
    public async Task PostBuild_FillWithoutSize_ShowsRowsAndColumnsRequired()
    {
        // Arrange
        using var host = new FleetTestHost();
        BusFormDriver form = await OpenAsync(host);

        // Act
        await form.PressAsync("[data-deck='lower'] [data-fill]");

        // Assert
        Assert.Contains("Rows is required.", form.Page.QuerySelector("[data-deck='lower']")!.TextContent);
        Assert.Contains("Columns is required.", form.Page.QuerySelector("[data-deck='lower']")!.TextContent);
        Assert.Empty(form.SeatLabels("lower"));
    }

    [Fact]
    public async Task PostSave_UpperDeckUnticked_AsksThenRemovesItsSeats()
    {
        // Arrange
        using var host = new FleetTestHost();
        BusFormDriver form = await OpenAsync(host);
        await form.NameSeatClassesAsync("Lower berth");
        await form.FillDeckAsync("lower", 2, 2, "");
        await form.FillDeckAsync("upper", 6, 3, "1");
        form.Tick("Input.HasUpperDeck", "true", false);

        // Act
        await form.PressAsync("[data-save]");
        string? question = form.Text("[data-question-text]");
        await form.PressAsync("[data-question-confirm]");

        // Assert
        Assert.Equal("Remove the upper deck and its 18 seats?", question);
        Assert.Empty(form.SeatLabels("upper"));
        Assert.Equal(4, form.SeatLabels("lower").Length);
        Assert.Empty(host.Fleet.Requests);
    }

    [Fact]
    public async Task PostBuild_RemoveSeatClassWithSeats_RefusesAndSaysHowMany()
    {
        // Arrange
        using var host = new FleetTestHost();
        BusFormDriver form = await OpenAsync(host);
        await form.NameSeatClassesAsync("Lower berth", "Upper berth");
        await form.FillDeckAsync("lower", 6, 3, "1");

        // Act
        await form.PressAsync("[data-seat-class-row]:first-of-type [data-remove-seat-class]");

        // Assert
        Assert.Equal("Lower berth still has 18 seats. Change their class first.", form.Text("[data-seat-class-error]"));
        Assert.Equal(2, form.Page.QuerySelectorAll("[data-seat-class-name]").Length);
    }

    [Fact]
    public async Task PostBuild_RenameSeatClass_RenamesItOnEverySeat()
    {
        // Arrange
        using var host = new FleetTestHost();
        BusFormDriver form = await OpenAsync(host);
        await form.NameSeatClassesAsync("Lower berth");
        await form.FillDeckAsync("lower", 2, 2, "");
        form.Set("Input.SeatClasses[0]", "Sleeper berth");

        // Act
        await form.PressAsync("[data-add-seat-class]");

        // Assert
        Assert.All(form.SeatClasses("lower"), seatClass => Assert.Equal("Sleeper berth", seatClass));
    }

    [Theory]
    [InlineData("duplicate-seat", "Seat number 1a is used twice.")]
    [InlineData("seat-with-space", "Seat number must be 1 to 5 letters or numbers, like 1A or U12.")]
    [InlineData("empty-seat", "Seat number must be 1 to 5 letters or numbers, like 1A or U12.")]
    [InlineData("long-seat", "Seat number must be 1 to 5 letters or numbers, like 1A or U12.")]
    [InlineData("too-many-seats", "A bus can have at most 60 seats. You have 72. Remove 12 seats.")]
    [InlineData("class-without-seats", "Upper berth has no seats. Give it at least one seat, or remove it.")]
    [InlineData("no-seats", "Add at least one seat.")]
    [InlineData("class-twice", "Seat class lower is listed twice.")]
    [InlineData("short-registration", "Registration number must have 6 to 15 letters and numbers. Spaces and hyphens are allowed.")]
    public async Task PostSave_PageCheckFails_ShowsMessageAndSendsNothing(string mistake, string expected)
    {
        // Arrange
        using var host = new FleetTestHost();
        BusFormDriver form = await OpenAsync(host);
        await form.FillShivneriSleeperAsync();
        await MakeMistakeAsync(form, mistake);

        // Act
        await form.PressAsync("[data-save]");

        // Assert
        Assert.Contains(expected, form.Page.QuerySelector("[data-bus-form]")!.TextContent);
        Assert.Equal("Shivneri Sleeper", ((AngleSharp.Html.Dom.IHtmlInputElement)form.Page.QuerySelector("[data-name]")!).Value);
        Assert.Empty(host.Fleet.Requests);
    }

    private static async Task<BusFormDriver> OpenAsync(FleetTestHost host)
    {
        HttpClient client = await host.SignInClientAsync(PortalSession.RoleOperator);
        IDocument page = await new FormClient(client).GetPageAsync(PageAddress);
        return new BusFormDriver(client, page);
    }

    // Each mistake from the page-checks example, made on the otherwise valid two-deck bus.
    private static async Task MakeMistakeAsync(BusFormDriver form, string mistake)
    {
        switch (mistake)
        {
            case "duplicate-seat":
                form.Set(form.SeatField("1B", "Label"), "1a");
                break;
            case "seat-with-space":
                form.Set(form.SeatField("1B", "Label"), "1 B");
                break;
            case "empty-seat":
                form.Set(form.SeatField("1B", "Label"), "");
                break;
            case "long-seat":
                form.Set(form.SeatField("1B", "Label"), "ABCDEF");
                break;
            case "too-many-seats":
                await form.FillDeckAsync("lower", 6, 6, "");
                await form.PressAsync("[data-question-confirm]");
                await form.FillDeckAsync("upper", 6, 6, "");
                await form.PressAsync("[data-question-confirm]");
                break;
            case "class-without-seats":
                form.Set("Input.Upper.ClassForAll", "Lower berth");
                await form.PressAsync("[data-deck='upper'] [data-deck-class-apply]");
                break;
            case "no-seats":
                form.Tick("Input.HasUpperDeck", "true", false);
                await form.PressAsync("[data-save]");
                await form.PressAsync("[data-question-confirm]");
                await form.FillDeckAsync("lower", 1, 1, "");
                await form.PressAsync("[data-question-confirm]");
                await form.PressAsync("[data-seat='1A'] [data-remove-seat]");
                break;
            case "class-twice":
                form.Set("Input.SeatClasses[1]", "lower");
                form.Set("Input.SeatClasses[0]", "Lower");
                break;
            default:
                form.Set("Input.RegistrationNumber", "MH12A");
                break;
        }
    }

    private static string[] Texts(IDocument page, string selector)
    {
        return page.QuerySelectorAll(selector).Select(element => element.TextContent.Trim()).ToArray();
    }
}
