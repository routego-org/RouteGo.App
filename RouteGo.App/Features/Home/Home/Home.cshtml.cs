namespace RouteGo.App.Features.Home.Home;

// The fields of Identity's Profile the home page uses; the rest of the answer is ignored.
public sealed record HomeProfile(string FullName);

public sealed class HomeHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HomeHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// The signed-in person's full name from their profile, or null when the profile cannot be
    /// loaded — the welcome then uses the email, and no error is shown.
    /// </summary>
    public async Task<string?> GetFullNameAsync(CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var request = new HttpRequestMessage(HttpMethod.Get, "me");
        try
        {
            HttpResponseMessage response = await identity.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
            ApiResult<HomeProfile> profile = await response.ReadApiResultAsync<HomeProfile>(cancellationToken);
            if (!profile.IsSuccess)
            {
                return null;
            }

            return profile.Value!.FullName;
        }
        catch (HttpRequestException)
        {
            // A server error without a problem body: the name is a nicety, so Home still works.
            return null;
        }
    }
}

public sealed class HomeModel : PageModel
{
    private const string HomeAddress = "/";

    private readonly HomeHandler _handler;
    private readonly SearchFormHandler _searchHandler;
    private readonly IClock _clock;
    private CitySuggestions _fromSuggestions = CitySuggestions.None;
    private CitySuggestions _toSuggestions = CitySuggestions.None;

    public HomeModel(IHttpClientFactory httpClientFactory, IClock clock)
    {
        _handler = new HomeHandler(httpClientFactory);
        _searchHandler = new SearchFormHandler(httpClientFactory);
        _clock = clock;
    }

    /// <summary>The bus search form, the same for everyone; posted here so a failed check keeps the address "/".</summary>
    [BindProperty]
    public SearchFormInput Search { get; set; } = new SearchFormInput();

    public bool IsSignedIn { get; private set; }

    /// <summary>The name in the welcome: the profile's full name, or the session's email.</summary>
    public string WelcomeName { get; private set; } = "";

    /// <summary>The role as the person reads it (Customer, Operator, Admin).</summary>
    public string RoleLabel { get; private set; } = "";

    /// <summary>Admins see shortcuts to the admin pages on their home page.</summary>
    public bool IsAdmin { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Search.Date = SearchForm.WireDate(Today());
        await LoadWelcomeAsync(cancellationToken);
    }

    /// <summary>"Search": the results page when the checks pass, otherwise the home page again with the form's messages.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        SearchFormOutcome outcome = await _searchHandler.SubmitAsync(Search, Today(), cancellationToken);
        if (outcome.ResultsRoute != null)
        {
            return RedirectToPage(SearchForm.ResultsPage, outcome.ResultsRoute);
        }

        SearchFormChecks.AddErrors(ModelState, outcome.Errors);
        _fromSuggestions = outcome.FromSuggestions;
        _toSuggestions = outcome.ToSuggestions;
        await LoadWelcomeAsync(cancellationToken);
        return Page();
    }

    public SearchFormView SearchView()
    {
        return new SearchFormView
        {
            Input = Search,
            PostAddress = HomeAddress,
            Today = Today(),
            ShowsTitle = true,
            FromSuggestions = _fromSuggestions,
            ToSuggestions = _toSuggestions,
        };
    }

    private DateOnly Today()
    {
        return IndiaTime.Today(_clock.UtcNow);
    }

    private async Task LoadWelcomeAsync(CancellationToken cancellationToken)
    {
        IsSignedIn = User.Identity != null && User.Identity.IsAuthenticated;
        if (!IsSignedIn)
        {
            return;
        }

        IsAdmin = User.IsInRole(PortalSession.RoleAdmin);
        RoleLabel = Messages.RoleLabel(User.FindFirst(PortalSession.RoleClaim)!.Value);
        string? fullName = await _handler.GetFullNameAsync(cancellationToken);
        WelcomeName = User.FindFirst(PortalSession.EmailClaim)!.Value;
        if (!string.IsNullOrEmpty(fullName))
        {
            WelcomeName = fullName;
        }
    }
}
