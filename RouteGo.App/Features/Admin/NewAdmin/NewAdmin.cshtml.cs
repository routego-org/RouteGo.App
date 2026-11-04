namespace RouteGo.App.Features.Admin.NewAdmin;

public sealed class NewAdminInput   // mutable class: must round-trip on re-render
{
    public string Email { get; set; } = "";

    public string Password { get; set; } = "";

    public string FullName { get; set; } = "";

    public string? Phone { get; set; }
}

public sealed class NewAdminValidator : AbstractValidator<NewAdminInput>
{
    public const int FullNameMinLength = 2;
    public const int FullNameMaxLength = 100;

    // Identity's field rules for a new account, checked before sending so a mistake is shown at
    // once; Identity checks them again and decides everything else.
    public const string PasswordPattern = "^(?=.*[A-Za-z])(?=.*[0-9]).{10,}$";
    public const string PhonePattern = @"^(\+91)?[6-9][0-9]{9}$";

    public NewAdminValidator()
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

public sealed record NewAdminRequest(string Email, string Password, string FullName)
{
    public string? Phone { get; init; }
}

// The field of Identity's answer this page uses.
public sealed record NewAdminResponse(string Email);

public sealed class NewAdminHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public NewAdminHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Asks Identity to create an Active admin account; Identity sends no email.</summary>
    public async Task<ApiResult<NewAdminResponse>> CreateAdminAsync(NewAdminInput input, CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var body = new NewAdminRequest(input.Email, input.Password, input.FullName) { Phone = PhoneOrNull(input.Phone) };
        var request = new HttpRequestMessage(HttpMethod.Post, "auth/admin/users")
        {
            Content = JsonContent.Create(body, options: WireJson.Options),
        };
        HttpResponseMessage response = await identity.SendAsync(IdentityRequests.WithUserToken(request), cancellationToken);
        return await response.ReadApiResultAsync<NewAdminResponse>(cancellationToken);
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

public sealed class NewAdminModel : PageModel
{
    public const string EmailAlreadyUsedMessage =
        "An account already uses this email. Each email can have only one account and one role.";

    // The catalog's success.created for the entity "Admin account"; kept here because Common/ is
    // at its size budget.
    public const string CreatedMessage = "Admin account created.";

    private readonly NewAdminHandler _handler;
    private readonly NewAdminValidator _validator = new NewAdminValidator();

    public NewAdminModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new NewAdminHandler(httpClientFactory);
    }

    [BindProperty]
    public NewAdminInput Input { get; set; } = new NewAdminInput();

    public PageAlert? Alert { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(Input, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Input));
            return RenderAgain();
        }

        ApiResult<NewAdminResponse> api = await _handler.CreateAdminAsync(Input, cancellationToken);
        if (!api.IsSuccess)
        {
            ShowRefusal(api);
            return RenderAgain();
        }

        // The new account is shown in the users list, found by the email Identity stored.
        TempData[Flash.Key] = CreatedMessage;
        return RedirectToPage("/Admin/Users/Users", new { email = api.Value!.Email });
    }

    // The password is never sent back to the browser.
    private PageResult RenderAgain()
    {
        Input.Password = "";
        return Page();
    }

    private void ShowRefusal(ApiResult<NewAdminResponse> api)
    {
        if (api.Errors != null)
        {
            AddBackendFieldErrors(api.Errors);
            return;
        }

        if (api.Problem!.Code == "EMAIL_ALREADY_REGISTERED")
        {
            ModelState.AddModelError("Input.Email", EmailAlreadyUsedMessage);
            return;
        }

        Alert = SharedProblems.ToAlert(api.Problem, CorrelationId.Get(HttpContext));
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
        if (Input.FullName.Length < NewAdminValidator.FullNameMinLength)
        {
            return Messages.ValidationMinLength("Full name", NewAdminValidator.FullNameMinLength);
        }

        if (Input.FullName.Length > NewAdminValidator.FullNameMaxLength)
        {
            return Messages.ValidationMaxLength("Full name", NewAdminValidator.FullNameMaxLength);
        }

        return identityMessage;
    }
}
