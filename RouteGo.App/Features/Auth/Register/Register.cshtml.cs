namespace RouteGo.App.Features.Auth.Register;

public sealed class RegisterInput   // mutable class: must round-trip on re-render
{
    public string Email { get; set; } = "";

    public string Password { get; set; } = "";

    public string FullName { get; set; } = "";

    public string? Phone { get; set; }
}

public sealed class RegisterValidator : AbstractValidator<RegisterInput>
{
    public const int FullNameMinLength = 2;
    public const int FullNameMaxLength = 100;

    // Identity's field rules for a registration, checked before sending so a mistake is shown at
    // once; Identity checks them again and decides everything else.
    public const string PasswordPattern = "^(?=.*[A-Za-z])(?=.*[0-9]).{10,}$";
    public const string PhonePattern = @"^(\+91)?[6-9][0-9]{9}$";

    public RegisterValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Email"))
            .EmailAddress().WithMessage(Messages.ValidationFormat("email"));
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Password"))
            .Matches(PasswordPattern).WithMessage(Messages.ValidationPasswordPolicy);
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Full name"))
            .MinimumLength(FullNameMinLength).WithMessage(Messages.ValidationMinLength("Full name", FullNameMinLength))
            .MaximumLength(FullNameMaxLength).WithMessage(Messages.ValidationMaxLength("Full name", FullNameMaxLength));
        RuleFor(x => x.Phone)
            .Matches(PhonePattern).WithMessage(Messages.ValidationPhone)
            .When(x => !string.IsNullOrEmpty(x.Phone));
    }
}

public sealed record RegisterRequest(string Email, string Password, string FullName)
{
    public string? Phone { get; init; }
}

// The fields of Identity's UserSummary this page uses.
public sealed record RegisterResponse(string Email);

public sealed class RegisterHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public RegisterHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Asks Identity to create the customer account.</summary>
    public async Task<ApiResult<RegisterResponse>> HandleAsync(RegisterInput input, CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var request = new RegisterRequest(input.Email, input.Password, input.FullName) { Phone = PhoneOrNull(input.Phone) };
        HttpResponseMessage response = await identity.PostAsJsonAsync("auth/registrations", request, WireJson.Options, cancellationToken);
        return await response.ReadApiResultAsync<RegisterResponse>(cancellationToken);
    }

    // A mobile number left empty is sent as no phone, never as empty text.
    private static string? PhoneOrNull(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        return phone;
    }
}

public sealed class RegisterModel : PageModel
{
    private const string RegisteredEmailKey = "Register.Email";

    private readonly RegisterHandler _handler;
    private readonly RegisterValidator _validator = new RegisterValidator();

    public RegisterModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new RegisterHandler(httpClientFactory);
    }

    [BindProperty]
    public RegisterInput Input { get; set; } = new RegisterInput();

    /// <summary>Set after a successful registration: the email Identity created the account for.</summary>
    public string? RegisteredEmail { get; private set; }

    public PageAlert? Alert { get; private set; }

    public void OnGet()
    {
        RegisteredEmail = TempData[RegisteredEmailKey] as string;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(Input, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Input));
            return RenderAgain();
        }

        ApiResult<RegisterResponse> api = await _handler.HandleAsync(Input, cancellationToken);
        if (!api.IsSuccess)
        {
            if (api.Errors != null)
            {
                AddBackendFieldErrors(api.Errors);
            }
            else
            {
                Alert = RefusalAlert(api.Problem!, CorrelationId.Get(HttpContext));
            }

            return RenderAgain();
        }

        // The success message names the email exactly as Identity returned it.
        TempData[RegisteredEmailKey] = api.Value!.Email;
        return RedirectToPage("/Auth/Register/Register");
    }

    // The password is never sent back to the browser.
    private PageResult RenderAgain()
    {
        Input.Password = "";
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
            case "email":
                return Messages.ValidationFormat("email");
            case "password":
                return Messages.ValidationPasswordPolicy;
            case "fullname":
                return FullNameMessage(identityMessage);
            case "phone":
                return Messages.ValidationPhone;
            default:
                return null;
        }
    }

    // The length rule the value breaks, in the catalog's words; when it breaks neither (Identity
    // counts characters its own way), Identity's message is the only truthful one.
    private string FullNameMessage(string identityMessage)
    {
        if (Input.FullName.Length < RegisterValidator.FullNameMinLength)
        {
            return Messages.ValidationMinLength("Full name", RegisterValidator.FullNameMinLength);
        }

        if (Input.FullName.Length > RegisterValidator.FullNameMaxLength)
        {
            return Messages.ValidationMaxLength("Full name", RegisterValidator.FullNameMaxLength);
        }

        return identityMessage;
    }

    private static PageAlert RefusalAlert(ApiProblem problem, string correlationId)
    {
        if (problem.Code == "EMAIL_ALREADY_REGISTERED")
        {
            return PageAlert.Danger(Messages.ProblemEmailAlreadyRegistered).WithLink("Sign in", "/auth/login");
        }

        return SharedProblems.ToAlert(problem, correlationId);
    }
}
