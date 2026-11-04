namespace RouteGo.App.Features.Operator.Apply;

public sealed class ApplyInput   // mutable class: must round-trip on re-render
{
    public string Email { get; set; } = "";

    public string Password { get; set; } = "";

    public string FullName { get; set; } = "";

    public string? Phone { get; set; }

    public string CompanyName { get; set; } = "";

    public string ContactEmail { get; set; } = "";

    public string Gstin { get; set; } = "";

    /// <summary>
    /// Removes the spaces from the GSTIN and writes its letters in capitals, so a GSTIN typed as
    /// " 27aapfu0939f1zv " is checked and sent as "27AAPFU0939F1ZV".
    /// </summary>
    public void TidyGstin()
    {
        if (string.IsNullOrEmpty(Gstin))
        {
            return;
        }

        string withoutSpaces = Gstin.Replace(" ", "", StringComparison.Ordinal);
        Gstin = withoutSpaces.ToUpperInvariant();
    }
}

public sealed class ApplyValidator : AbstractValidator<ApplyInput>
{
    public const int MaxEmailLength = 254;
    public const int MinPasswordLength = 10;
    public const int FullNameMinLength = 2;
    public const int FullNameMaxLength = 100;
    public const int CompanyNameMinLength = 2;
    public const int CompanyNameMaxLength = 150;

    // Identity's field rules for an application, checked before sending so a mistake is shown at
    // once. \z, not $: a $ also matches before a trailing newline, which Identity refuses.
    public const string PhonePattern = @"^(\+91)?[6-9][0-9]{9}\z";
    public const string GstinPattern = @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]\z";

    public ApplyValidator()
    {
        RuleFor(x => x.Email).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Email"))
            .EmailAddress().WithMessage(Messages.ValidationFormat("email"))
            .MaximumLength(MaxEmailLength).WithMessage(Messages.ValidationFormat("email"));
        RuleFor(x => x.Password).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Password"))
            .Must(IsAcceptablePassword).WithMessage(Messages.ValidationPasswordPolicy);
        RuleFor(x => x.FullName).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Full name"))
            .Must(IsAtLeast(FullNameMinLength)).WithMessage(Messages.ValidationMinLength("Full name", FullNameMinLength))
            .Must(IsAtMost(FullNameMaxLength)).WithMessage(Messages.ValidationMaxLength("Full name", FullNameMaxLength));
        RuleFor(x => x.Phone)
            .Matches(PhonePattern).WithMessage(Messages.ValidationPhone)
            .When(x => !string.IsNullOrEmpty(x.Phone));
        RuleFor(x => x.CompanyName).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Company name"))
            .Must(IsAtLeast(CompanyNameMinLength)).WithMessage(Messages.ValidationMinLength("Company name", CompanyNameMinLength))
            .Must(IsAtMost(CompanyNameMaxLength)).WithMessage(Messages.ValidationMaxLength("Company name", CompanyNameMaxLength));
        RuleFor(x => x.ContactEmail).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Contact email"))
            .EmailAddress().WithMessage(Messages.ValidationFormat("contact email"))
            .MaximumLength(MaxEmailLength).WithMessage(Messages.ValidationFormat("contact email"));
        RuleFor(x => x.Gstin).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(Messages.ValidationRequired("GSTIN"))
            .Matches(GstinPattern).WithMessage(Messages.ValidationGstin);
    }

    /// <summary>
    /// The length of a name as Identity counts it: an emoji is one character, not the two UTF-16
    /// units .NET's Length reports.
    /// </summary>
    public static int CharacterCount(string text)
    {
        return text.EnumerateRunes().Count();
    }

    // Identity's password policy: at least 10 characters with an ASCII letter and an ASCII digit.
    private static bool IsAcceptablePassword(string password)
    {
        if (password.Length < MinPasswordLength)
        {
            return false;
        }

        return password.Any(char.IsAsciiLetter) && password.Any(char.IsAsciiDigit);
    }

    private static Func<string, bool> IsAtLeast(int length)
    {
        return text => CharacterCount(text) >= length;
    }

    private static Func<string, bool> IsAtMost(int length)
    {
        return text => CharacterCount(text) <= length;
    }
}

public sealed record ApplyRequest
{
    public required string Email { get; init; }

    public required string Password { get; init; }

    public required string FullName { get; init; }

    public required string? Phone { get; init; }

    public required string CompanyName { get; init; }

    public required string ContactEmail { get; init; }

    public required string Gstin { get; init; }
}

// The field of Identity's OperatorApplication this page uses.
public sealed record ApplyResponse(string Email);

public sealed class ApplyHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ApplyHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Asks Identity to create the operator account and its application.</summary>
    public async Task<ApiResult<ApplyResponse>> HandleAsync(ApplyInput input, CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        ApplyRequest request = ToRequest(input);
        HttpResponseMessage response = await identity.PostAsJsonAsync("operators/applications", request, WireJson.Options, cancellationToken);
        return await response.ReadApiResultAsync<ApplyResponse>(cancellationToken);
    }

    private static ApplyRequest ToRequest(ApplyInput input)
    {
        return new ApplyRequest
        {
            Email = input.Email,
            Password = input.Password,
            FullName = input.FullName,
            Phone = PhoneOrNull(input.Phone),
            CompanyName = input.CompanyName,
            ContactEmail = input.ContactEmail,
            Gstin = input.Gstin,
        };
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

public sealed class ApplyModel : PageModel
{
    private const string AppliedEmailKey = "Apply.Email";
    private const string ReapplyPath = "/operator/reapply";

    private readonly ApplyHandler _handler;
    private readonly ApplyValidator _validator = new ApplyValidator();

    public ApplyModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new ApplyHandler(httpClientFactory);
    }

    [BindProperty]
    public ApplyInput Input { get; set; } = new ApplyInput();

    /// <summary>Set after a successful application: the sign-in email Identity created the account for.</summary>
    public string? AppliedEmail { get; private set; }

    public PageAlert? Alert { get; private set; }

    public void OnGet()
    {
        AppliedEmail = TempData[AppliedEmailKey] as string;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Input.TidyGstin();
        ValidationResult validation = await _validator.ValidateAsync(Input, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Input));
            return RenderAgain();
        }

        ApiResult<ApplyResponse> api = await _handler.HandleAsync(Input, cancellationToken);
        if (!api.IsSuccess)
        {
            ShowRefusal(api);
            return RenderAgain();
        }

        // The success message names the email exactly as Identity returned it.
        TempData[AppliedEmailKey] = api.Value!.Email;
        return RedirectToPage("/Operator/Apply/Apply");
    }

    // The password is never sent back to the browser. The tidied GSTIN replaces the typed one, so
    // the field shows what was checked; its error, if any, stays.
    private PageResult RenderAgain()
    {
        Input.Password = "";
        ModelState.SetModelValue("Input.Gstin", Input.Gstin, Input.Gstin);
        return Page();
    }

    private void ShowRefusal(ApiResult<ApplyResponse> api)
    {
        if (api.Errors != null)
        {
            AddBackendFieldErrors(api.Errors);
            return;
        }

        ApiProblem problem = api.Problem!;
        if (problem.Code == "GSTIN_ALREADY_REGISTERED")
        {
            ModelState.AddModelError("Input.Gstin", Messages.ProblemGstinAlreadyRegistered);
            return;
        }

        if (problem.Code == "EMAIL_ALREADY_REGISTERED")
        {
            Alert = PageAlert.Danger("This email is already registered. If it is your operator account and your application was rejected, re-apply instead. Otherwise, sign in.")
                .WithLink("Re-apply", ReapplyPath)
                .WithLink("Sign in", SignInAddress.Path);
            return;
        }

        Alert = SharedProblems.ToAlert(problem, CorrelationId.Get(HttpContext));
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
                return LengthMessage("Full name", Input.FullName, ApplyValidator.FullNameMinLength, ApplyValidator.FullNameMaxLength) ?? identityMessage;
            case "phone":
                return Messages.ValidationPhone;
            case "companyname":
                return LengthMessage("Company name", Input.CompanyName, ApplyValidator.CompanyNameMinLength, ApplyValidator.CompanyNameMaxLength) ?? identityMessage;
            case "contactemail":
                return Messages.ValidationFormat("contact email");
            case "gstin":
                return Messages.ValidationGstin;
            default:
                return null;
        }
    }

    // The length rule the value breaks, in the catalog's words; null when it breaks neither, so
    // Identity refused it for another reason and its own message is the only truthful one.
    private static string? LengthMessage(string field, string value, int min, int max)
    {
        int length = ApplyValidator.CharacterCount(value);
        if (length < min)
        {
            return Messages.ValidationMinLength(field, min);
        }

        if (length > max)
        {
            return Messages.ValidationMaxLength(field, max);
        }

        return null;
    }
}
