namespace RouteGo.App.Features.Profile.Profile;

public sealed class ProfileInput   // mutable class: must round-trip on re-render
{
    public string FullName { get; set; } = "";

    public string? Phone { get; set; }
}

public sealed class ProfileValidator : AbstractValidator<ProfileInput>
{
    public const int FullNameMinLength = 2;
    public const int FullNameMaxLength = 100;

    // Identity's field rules for a profile, checked before sending so a mistake is shown at once.
    // \z, not $: a $ also matches before a trailing newline, which Identity refuses.
    public const string PhonePattern = @"^(\+91)?[6-9][0-9]{9}\z";

    public ProfileValidator()
    {
        RuleFor(x => x.FullName).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Full name"))
            .Must(IsLongEnough).WithMessage(Messages.ValidationMinLength("Full name", FullNameMinLength))
            .Must(IsShortEnough).WithMessage(Messages.ValidationMaxLength("Full name", FullNameMaxLength));
        RuleFor(x => x.Phone)
            .Matches(PhonePattern).WithMessage(Messages.ValidationPhone)
            .When(x => !string.IsNullOrEmpty(x.Phone));
    }

    /// <summary>
    /// The length of a full name as Identity counts it: an emoji is one character, not the two
    /// UTF-16 units .NET's Length reports.
    /// </summary>
    public static int CharacterCount(string fullName)
    {
        return fullName.EnumerateRunes().Count();
    }

    private static bool IsLongEnough(string fullName)
    {
        return CharacterCount(fullName) >= FullNameMinLength;
    }

    private static bool IsShortEnough(string fullName)
    {
        return CharacterCount(fullName) <= FullNameMaxLength;
    }
}

// Identity replaces the whole profile, so both fields are always sent; a null phone removes it.
public sealed record ReplaceProfileRequest(string FullName, string? Phone);

// The fields of Identity's Profile this page uses; the rest of the answer is ignored.
public sealed record ProfileResponse
{
    public required string Email { get; init; }

    public required string FullName { get; init; }

    /// <summary>+91 followed by 10 digits, or null when there is none.</summary>
    public required string? Phone { get; init; }

    public required string Role { get; init; }
}

/// <summary>The part of the profile that is shown but cannot be changed.</summary>
public sealed record ProfileAccount(string Email, string RoleLabel);

public sealed class ProfileHandler
{
    private const string ProfilePath = "me";

    private readonly IHttpClientFactory _httpClientFactory;

    public ProfileHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>The signed-in person's profile, as Identity holds it.</summary>
    public async Task<ApiResult<ProfileResponse>> GetProfileAsync(CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var request = new HttpRequestMessage(HttpMethod.Get, ProfilePath);
        HttpResponseMessage response = await identity.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<ProfileResponse>(cancellationToken);
    }

    /// <summary>Replaces the signed-in person's full name and mobile number.</summary>
    public async Task<ApiResult<ProfileResponse>> ReplaceProfileAsync(ProfileInput input, CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var body = new ReplaceProfileRequest(input.FullName, PhoneOrNull(input.Phone));
        var request = new HttpRequestMessage(HttpMethod.Put, ProfilePath)
        {
            Content = JsonContent.Create(body, options: WireJson.Options),
        };
        HttpResponseMessage response = await identity.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<ProfileResponse>(cancellationToken);
    }

    // An emptied mobile number is sent as no phone, never as empty text: that is what removes it.
    private static string? PhoneOrNull(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        return phone;
    }
}

public sealed class ProfileModel : PageModel
{
    private const string ProfileEntity = "Profile";

    private readonly ProfileHandler _handler;
    private readonly ProfileValidator _validator = new ProfileValidator();

    public ProfileModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new ProfileHandler(httpClientFactory);
    }

    [BindProperty]
    public ProfileInput Input { get; set; } = new ProfileInput();

    /// <summary>Email and role; null when the profile could not be loaded, and then no form is shown.</summary>
    public ProfileAccount? Account { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApiResult<ProfileResponse> api = await _handler.GetProfileAsync(cancellationToken);
        if (!api.IsSuccess)
        {
            Alert = SharedProblems.ToAlert(api.Problem!, CorrelationId.Get(HttpContext));
            return;
        }

        ProfileResponse profile = api.Value!;
        Account = new ProfileAccount(profile.Email, Messages.RoleLabel(profile.Role));
        Input.FullName = profile.FullName;
        Input.Phone = profile.Phone;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(Input, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Input));
            return RenderAgain();
        }

        ApiResult<ProfileResponse> api = await _handler.ReplaceProfileAsync(Input, cancellationToken);
        if (!api.IsSuccess)
        {
            if (api.Errors != null)
            {
                AddBackendFieldErrors(api.Errors);
            }
            else
            {
                Alert = SharedProblems.ToAlert(api.Problem!, CorrelationId.Get(HttpContext));
            }

            return RenderAgain();
        }

        // The page loads the profile again after the redirect, so it shows what Identity now holds.
        TempData[Flash.Key] = Messages.SuccessUpdated(ProfileEntity);
        return RedirectToPage("/Profile/Profile/Profile");
    }

    // Email and role never change, so a re-render takes them from the session instead of asking
    // Identity again; field errors then still show while Identity is down.
    private PageResult RenderAgain()
    {
        string email = User.FindFirst(PortalSession.EmailClaim)!.Value;
        string role = User.FindFirst(PortalSession.RoleClaim)!.Value;
        Account = new ProfileAccount(email, Messages.RoleLabel(role));
        return Page();
    }

    // Identity's field errors are shown with the page's own wording for that field; a field the
    // page does not show appears at the top with Identity's message.
    private void AddBackendFieldErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (KeyValuePair<string, string[]> error in errors)
        {
            string? message = FieldMessage(error.Key, error.Value[0]);
            if (message is null)
            {
                Alert = PageAlert.Danger(error.Value[0]);
                continue;
            }

            ModelState.AddModelError("Input." + error.Key, message);
        }
    }

    private string? FieldMessage(string field, string identityMessage)
    {
        switch (field.ToLowerInvariant())
        {
            case "fullname":
                return FullNameMessage(identityMessage);
            case "phone":
                return Messages.ValidationPhone;
            default:
                return null;
        }
    }

    // The length rule the value breaks, in the catalog's words; when it breaks neither, Identity
    // refused it for another reason and its own message is the only truthful one.
    private string FullNameMessage(string identityMessage)
    {
        int length = ProfileValidator.CharacterCount(Input.FullName);
        if (length < ProfileValidator.FullNameMinLength)
        {
            return Messages.ValidationMinLength("Full name", ProfileValidator.FullNameMinLength);
        }

        if (length > ProfileValidator.FullNameMaxLength)
        {
            return Messages.ValidationMaxLength("Full name", ProfileValidator.FullNameMaxLength);
        }

        return identityMessage;
    }
}
