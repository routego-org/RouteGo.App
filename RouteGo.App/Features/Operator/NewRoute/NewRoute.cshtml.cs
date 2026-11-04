using Microsoft.AspNetCore.Mvc.Rendering;

namespace RouteGo.App.Features.Operator.NewRoute;

public sealed class NewRouteModel : PageModel
{
    public const string CancelAddress = "/operator/routes";
    public const string UnknownCityMessage = "One of the cities is no longer available. We have loaded the city list again. Please choose the cities again.";

    private const string NewRoutePage = "/Operator/NewRoute/NewRoute";
    private const string RoutePage = "/Operator/Route/Route";
    private const string Entity = "route";
    private const string EntityLabel = "Route";
    private const string UnknownCity = "UNKNOWN_CITY";

    // How Fleet names the fields in its field errors: "distanceKm", and a stop's city as "stops[2].cityId".
    private const string DistanceField = "distanceKm";
    private const string CityFieldStart = "stops[";
    private const string CityFieldEnd = "].cityId";

    private readonly NewRouteHandler _handler;

    public NewRouteModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new NewRouteHandler(httpClientFactory);
    }

    [BindProperty]
    public NewRouteInput Input { get; set; } = new NewRouteInput();

    /// <summary>The cities to choose from; null when they could not be loaded, and then there is no form.</summary>
    public IReadOnlyList<City>? Cities { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Input.EnsureFirstAndLast();
        await LoadCitiesAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Input.EnsureFirstAndLast();
        bool hasCities = await LoadCitiesAsync(cancellationToken);
        if (!hasCities)
        {
            return Page();
        }

        var validator = new NewRouteValidator(Cities!);
        ValidationResult validation = await validator.ValidateAsync(Input, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Input));
            return Page();
        }

        ApiResult<CreatedRoute> api = await _handler.CreateRouteAsync(Input, cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = Messages.SuccessCreated(EntityLabel);
            return RedirectToPage(RoutePage, new { routeId = api.Value!.RouteId });
        }

        await ShowRefusalAsync(api, cancellationToken);
        return Page();
    }

    // Adding or removing a stop only changes the form, so the page is shown again with what was
    // typed; the stops' positions move, so the posted values are read from Input, not ModelState.
    public async Task<IActionResult> OnPostAddStopAsync(CancellationToken cancellationToken)
    {
        Input.EnsureFirstAndLast();
        Input.AddStop();
        ModelState.Clear();
        await LoadCitiesAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveStopAsync(int stop, CancellationToken cancellationToken)
    {
        Input.EnsureFirstAndLast();
        Input.RemoveStop(stop);
        ModelState.Clear();
        await LoadCitiesAsync(cancellationToken);
        return Page();
    }

    /// <summary>Where "Create route" posts; explicit, because a re-rendered page may be at an add or remove address.</summary>
    public string CreateAddress()
    {
        return Url.Page(NewRoutePage)!;
    }

    public string AddStopAddress()
    {
        return Url.Page(NewRoutePage, "AddStop")!;
    }

    public string RemoveStopAddress(int index)
    {
        return Url.Page(NewRoutePage, "RemoveStop", new { stop = index })!;
    }

    /// <summary>The city choices, "{city name}, {state}" valued by city id.</summary>
    public List<SelectListItem> CityChoices()
    {
        var choices = new List<SelectListItem>();
        foreach (City city in Cities!)
        {
            choices.Add(new SelectListItem(city.Label(), city.CityId.ToString()));
        }

        return choices;
    }

    private async Task<bool> LoadCitiesAsync(CancellationToken cancellationToken)
    {
        ApiResult<CityList> api = await _handler.ListCitiesAsync(cancellationToken);
        if (!api.IsSuccess)
        {
            Cities = null;
            Alert = FleetDisplay.ToAlert(api.Problem!, Entity, CorrelationId.Get(HttpContext));
            return false;
        }

        Cities = api.Value!.Items;
        return true;
    }

    private async Task ShowRefusalAsync(ApiResult<CreatedRoute> api, CancellationToken cancellationToken)
    {
        if (api.Errors != null)
        {
            AddFleetFieldErrors(api.Errors, api.ErrorsTitle);
            return;
        }

        ApiProblem problem = api.Problem!;
        if (problem.Code == UnknownCity)
        {
            ClearCityChoices();
            bool hasCities = await LoadCitiesAsync(cancellationToken);
            if (hasCities)
            {
                Alert = PageAlert.Danger(UnknownCityMessage);
            }

            return;
        }

        Alert = FleetDisplay.ToAlert(problem, Entity, CorrelationId.Get(HttpContext));
    }

    // The posted choice would win over the emptied one when the page is drawn again.
    private void ClearCityChoices()
    {
        Input.ClearCities();
        for (int index = 0; index < Input.Stops.Count; index++)
        {
            ModelState.Remove(nameof(Input) + "." + NewRouteValidator.StopField(index, nameof(NewRouteStopInput.CityId)));
        }
    }

    // Fleet's field errors in the page's own words at the field; a field the page does not show as
    // typed (a stop's minutes from start, the list of stops) is told at the top with Fleet's title.
    private void AddFleetFieldErrors(IReadOnlyDictionary<string, string[]> errors, string? title)
    {
        foreach (KeyValuePair<string, string[]> error in errors)
        {
            if (string.Equals(error.Key, DistanceField, StringComparison.OrdinalIgnoreCase))
            {
                string message = Messages.ValidationRange(NewRouteValidator.DistanceLabel, NewRouteValidator.MinDistanceKm, NewRouteValidator.MaxDistanceKm);
                ModelState.AddModelError(nameof(Input) + "." + nameof(NewRouteInput.DistanceKm), message);
                continue;
            }

            int? stopIndex = CityStopIndex(error.Key);
            if (stopIndex != null)
            {
                string field = NewRouteValidator.StopField(stopIndex.Value, nameof(NewRouteStopInput.CityId));
                ModelState.AddModelError(nameof(Input) + "." + field, Messages.ValidationRequired(NewRouteValidator.CityLabel));
                continue;
            }

            Alert = PageAlert.Danger(title ?? error.Value[0]);
        }
    }

    // "stops[2].cityId" -> 2, when the page has that stop; null for any other field.
    private int? CityStopIndex(string field)
    {
        bool isCityField = field.StartsWith(CityFieldStart, StringComparison.OrdinalIgnoreCase)
            && field.EndsWith(CityFieldEnd, StringComparison.OrdinalIgnoreCase);
        if (!isCityField)
        {
            return null;
        }

        string indexText = field.Substring(CityFieldStart.Length, field.Length - CityFieldStart.Length - CityFieldEnd.Length);
        int? index = NewRouteInput.WholeNumber(indexText);
        if (index is null || index >= Input.Stops.Count)
        {
            return null;
        }

        return index;
    }
}
