using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Admin.OperatorProfiles;

/// <summary>Fleet's company-name rule, checked before the search so a long name shows a field message.</summary>
public sealed class OperatorProfilesQueryValidator : AbstractValidator<OperatorProfilesQuery>
{
    public const int MaxCompanyNameLength = 150;
    public const string CompanyNameLabel = "Company name";

    public OperatorProfilesQueryValidator()
    {
        RuleFor(x => x.CompanyName).MaximumLength(MaxCompanyNameLength)
            .WithMessage(Messages.ValidationMaxLength(CompanyNameLabel, MaxCompanyNameLength));
    }
}

public sealed class OperatorProfilesModel : PageModel
{
    public const string Items = "companies";
    public const string AllItems = "approved companies";

    private const string ListPageName = "/Admin/OperatorProfiles/OperatorProfiles";
    private const string TripSearchAddress = "/admin/trips";
    private const string CompanyNameField = "companyName";

    private readonly OperatorProfilesHandler _handler;
    private readonly OperatorProfilesQueryValidator _validator = new OperatorProfilesQueryValidator();

    public OperatorProfilesModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new OperatorProfilesHandler(httpClientFactory);
    }

    [BindProperty(SupportsGet = true)]
    public OperatorProfilesQuery Query { get; set; } = new OperatorProfilesQuery();

    /// <summary>The loaded list; null when it could not be loaded.</summary>
    public OperatorProfilesPage? Profiles { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(Query, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Query));
            return;
        }

        ApiResult<OperatorProfilesPage> api = await _handler.SearchOperatorProfilesAsync(Query, cancellationToken);
        if (api.IsSuccess)
        {
            Profiles = api.Value;
            return;
        }

        if (api.Errors != null)
        {
            ShowSearchErrors(api);
            return;
        }

        Alert = SharedProblems.ToAlert(api.Problem!, CorrelationId.Get(HttpContext));
    }

    /// <summary>The list address with the current search and the given page.</summary>
    public string ListAddress(int page)
    {
        return Url.Page(ListPageName, new { companyName = Query.CompanyName, pageNumber = page })!;
    }

    /// <summary>Trip search filtered to the company; the name only shows which company is chosen.</summary>
    public static string TripsAddress(OperatorProfile profile)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["operatorId"] = profile.OperatorId,
            ["operatorName"] = profile.CompanyName,
        };
        return QueryHelpers.AddQueryString(TripSearchAddress, parameters);
    }

    /// <summary>"4.5 out of 5": the rating with one decimal place.</summary>
    public static string RatingText(decimal rating)
    {
        string value = rating.ToString("0.0", CultureInfo.InvariantCulture);
        return value + " out of 5";
    }

    // Fleet's own refusal of the search text shows the page's length message on the field; any other
    // refusal (a page number typed into the address) is told at the top.
    private void ShowSearchErrors(ApiResult<OperatorProfilesPage> api)
    {
        bool namesCompanyName = api.Errors!.Keys.Any(field => string.Equals(field, CompanyNameField, StringComparison.OrdinalIgnoreCase));
        if (namesCompanyName)
        {
            string message = Messages.ValidationMaxLength(OperatorProfilesQueryValidator.CompanyNameLabel, OperatorProfilesQueryValidator.MaxCompanyNameLength);
            ModelState.AddModelError(nameof(Query) + "." + nameof(OperatorProfilesQuery.CompanyName), message);
            return;
        }

        Alert = PageAlert.Danger(api.ErrorsTitle ?? Messages.ProblemInternalError(CorrelationId.Get(HttpContext)));
    }
}
