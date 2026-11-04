namespace RouteGo.App.Features.Operator.Reapply;

public sealed class ReapplyInput   // mutable class: must round-trip on re-render
{
    public string Email { get; set; } = "";

    public string Password { get; set; } = "";

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

public sealed class ReapplyValidator : AbstractValidator<ReapplyInput>
{
    public const int MaxEmailLength = 254;
    public const int CompanyNameMinLength = 2;
    public const int CompanyNameMaxLength = 150;

    // Identity's field rules for a re-application, checked before sending so a mistake is shown at
    // once. \z, not $: a $ also matches before a trailing newline, which Identity refuses. The
    // password is only checked for presence: it proves who is re-applying, and the policy applied
    // when it was chosen.
    public const string GstinPattern = @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]\z";

    public ReapplyValidator()
    {
        RuleFor(x => x.Email).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Email"))
            .EmailAddress().WithMessage(Messages.ValidationFormat("email"))
            .MaximumLength(MaxEmailLength).WithMessage(Messages.ValidationFormat("email"));
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Password"));
        RuleFor(x => x.CompanyName).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Company name"))
            .Must(IsLongEnough).WithMessage(Messages.ValidationMinLength("Company name", CompanyNameMinLength))
            .Must(IsShortEnough).WithMessage(Messages.ValidationMaxLength("Company name", CompanyNameMaxLength));
        RuleFor(x => x.ContactEmail).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Contact email"))
            .EmailAddress().WithMessage(Messages.ValidationFormat("contact email"))
            .MaximumLength(MaxEmailLength).WithMessage(Messages.ValidationFormat("contact email"));
        RuleFor(x => x.Gstin).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(Messages.ValidationRequired("GSTIN"))
            .Matches(GstinPattern).WithMessage(Messages.ValidationGstin);
    }

    /// <summary>
    /// The length of a company name as Identity counts it: an emoji is one character, not the two
    /// UTF-16 units .NET's Length reports.
    /// </summary>
    public static int CharacterCount(string text)
    {
        return text.EnumerateRunes().Count();
    }

    private static bool IsLongEnough(string companyName)
    {
        return CharacterCount(companyName) >= CompanyNameMinLength;
    }

    private static bool IsShortEnough(string companyName)
    {
        return CharacterCount(companyName) <= CompanyNameMaxLength;
    }
}

public sealed record ReapplyRequest
{
    public required string Email { get; init; }

    public required string Password { get; init; }

    public required string CompanyName { get; init; }

    public required string ContactEmail { get; init; }

    public required string Gstin { get; init; }
}

/// <summary>An earlier application of the same operator that an admin rejected.</summary>
public sealed record EarlierRejection(DateTimeOffset RejectedAt, string Reason);

// The field of Identity's OperatorApplication this page uses: the earlier rejections, newest first.
public sealed record ReapplyResponse(IReadOnlyList<EarlierRejection> PreviousRejections);

public sealed class ReapplyHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ReapplyHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Asks Identity to send the rejected operator's new application to review.</summary>
    public async Task<ApiResult<ReapplyResponse>> HandleAsync(ReapplyInput input, CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var request = new ReapplyRequest
        {
            Email = input.Email,
            Password = input.Password,
            CompanyName = input.CompanyName,
            ContactEmail = input.ContactEmail,
            Gstin = input.Gstin,
        };
        HttpResponseMessage response = await identity.PostAsJsonAsync("operators/reapplications", request, WireJson.Options, cancellationToken);
        return await response.ReadApiResultAsync<ReapplyResponse>(cancellationToken);
    }
}

public sealed class ReapplyModel : PageModel
{
    private const string EarlierRejectionsKey = "Reapply.EarlierRejections";

    private readonly ReapplyHandler _handler;
    private readonly ReapplyValidator _validator = new ReapplyValidator();

    public ReapplyModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new ReapplyHandler(httpClientFactory);
    }

    [BindProperty]
    public ReapplyInput Input { get; set; } = new ReapplyInput();

    /// <summary>
    /// Set after a successful re-application: the earlier rejections Identity listed, newest first
    /// (empty when it listed none). Null while the form is shown.
    /// </summary>
    public IReadOnlyList<EarlierRejection>? EarlierRejections { get; private set; }

    public PageAlert? Alert { get; private set; }

    public void OnGet()
    {
        // The rejections travel through the redirect as JSON in the encrypted TempData cookie.
        if (TempData[EarlierRejectionsKey] is string json)
        {
            EarlierRejections = JsonSerializer.Deserialize<List<EarlierRejection>>(json, WireJson.Options);
        }
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

        ApiResult<ReapplyResponse> api = await _handler.HandleAsync(Input, cancellationToken);
        if (!api.IsSuccess)
        {
            ShowRefusal(api);
            return RenderAgain();
        }

        TempData[EarlierRejectionsKey] = JsonSerializer.Serialize(api.Value!.PreviousRejections, WireJson.Options);
        return RedirectToPage("/Operator/Reapply/Reapply");
    }

    // The password is never sent back to the browser. The tidied GSTIN replaces the typed one, so
    // the field shows what was checked; its error, if any, stays.
    private PageResult RenderAgain()
    {
        Input.Password = "";
        ModelState.SetModelValue("Input.Gstin", Input.Gstin, Input.Gstin);
        return Page();
    }

    private void ShowRefusal(ApiResult<ReapplyResponse> api)
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

        Alert = RefusalAlert(problem, CorrelationId.Get(HttpContext));
    }

    private PageAlert RefusalAlert(ApiProblem problem, string correlationId)
    {
        switch (problem.Code)
        {
            case "INVALID_CREDENTIALS":
                return PageAlert.Danger(Messages.ProblemInvalidCredentials);
            case "ACCOUNT_LOCKED":
                return TimedAlert(problem, "lockedUntil", Messages.ProblemAccountLocked, correlationId);
            case "REAPPLICATION_TOO_SOON":
                return TimedAlert(problem, "reapplyAllowedAt", TooSoonMessage, correlationId);
            case "OPERATOR_NOT_REJECTED":
                return NotRejectedAlert(problem, correlationId);
            default:
                return SharedProblems.ToAlert(problem, correlationId);
        }
    }

    // A refusal whose words name a moment from the problem, shown in IST; without that moment the
    // words cannot be completed, so the generic message is shown instead.
    private static PageAlert TimedAlert(
        ApiProblem problem, string extension, Func<string, string> message, string correlationId)
    {
        string? moment = IndiaTime.FormatIso(problem.GetExtension(extension));
        if (moment is null)
        {
            return SharedProblems.ToAlert(problem, correlationId);
        }

        return PageAlert.Danger(message(moment));
    }

    private static string TooSoonMessage(string reapplyAllowedAt)
    {
        return $"You can re-apply from {reapplyAllowedAt}.";
    }

    // What to tell an account that is not a rejected operator depends on where it is now.
    private PageAlert NotRejectedAlert(ApiProblem problem, string correlationId)
    {
        switch (problem.GetExtension("currentStatus"))
        {
            case "pendingApproval":
                return PageAlert.Info("Your application is already waiting for review. We will email you when it is decided.");
            case "pendingVerification":
                string resendHref = "/auth/resend-verification?email=" + Uri.EscapeDataString(Input.Email);
                return PageAlert.Warning("Please verify your email first. Check your inbox for the verification link.")
                    .WithLink("Send the link again", resendHref);
            case "active":
                return PageAlert.Info("This account can already sign in. There is nothing to re-apply for.")
                    .WithLink("Sign in", SignInAddress.Path);
            case "suspended":
                return PageAlert.Danger(Messages.ProblemAccountSuspended);
            default:
                return SharedProblems.ToAlert(problem, correlationId);
        }
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
                return Messages.ValidationRequired("Password");
            case "companyname":
                return CompanyNameMessage(identityMessage);
            case "contactemail":
                return Messages.ValidationFormat("contact email");
            case "gstin":
                return Messages.ValidationGstin;
            default:
                return null;
        }
    }

    // The length rule the value breaks, in the catalog's words; when it breaks neither, Identity
    // refused it for another reason and its own message is the only truthful one.
    private string CompanyNameMessage(string identityMessage)
    {
        int length = ReapplyValidator.CharacterCount(Input.CompanyName);
        if (length < ReapplyValidator.CompanyNameMinLength)
        {
            return Messages.ValidationMinLength("Company name", ReapplyValidator.CompanyNameMinLength);
        }

        if (length > ReapplyValidator.CompanyNameMaxLength)
        {
            return Messages.ValidationMaxLength("Company name", ReapplyValidator.CompanyNameMaxLength);
        }

        return identityMessage;
    }
}
