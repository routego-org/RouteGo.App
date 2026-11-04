namespace RouteGo.App.Features.Auth.Login;

public sealed class LoginInput   // mutable class: must round-trip on re-render
{
    public string Email { get; set; } = "";

    public string Password { get; set; } = "";
}

public sealed class LoginValidator : AbstractValidator<LoginInput>
{
    public LoginValidator()
    {
        // The same shape checks Identity makes before it counts an attempt; the decision about the
        // credentials is Identity's.
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Email"))
            .EmailAddress().WithMessage(Messages.ValidationFormat("email"));
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Password"));
    }
}

public sealed record LoginRequest(string Email, string Password);

public sealed class LoginHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public LoginHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Asks Identity for a token pair for the email and password.</summary>
    public async Task<ApiResult<TokenPair>> HandleAsync(LoginInput input, CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var request = new LoginRequest(input.Email, input.Password);
        HttpResponseMessage response = await identity.PostAsJsonAsync("auth/sessions", request, WireJson.Options, cancellationToken);
        return await response.ReadApiResultAsync<TokenPair>(cancellationToken);
    }
}

public sealed class LoginModel : PageModel
{
    private const string EmailField = "email";

    private readonly LoginHandler _handler;
    private readonly LoginValidator _validator = new LoginValidator();

    public LoginModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new LoginHandler(httpClientFactory);
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new LoginInput();

    /// <summary>Where to go after signing in; kept in the address so it survives a failed attempt.</summary>
    [BindProperty(SupportsGet = true, Name = SignInAddress.ReturnUrlParameter)]
    public string? ReturnUrl { get; set; }

    /// <summary>Why the person was sent here, if another page sent them.</summary>
    [BindProperty(SupportsGet = true, Name = SignInAddress.ReasonParameter)]
    public string? Reason { get; set; }

    /// <summary>The arrival message or the refusal, shown above the form.</summary>
    public PageAlert? Alert { get; private set; }

    public void OnGet()
    {
        Alert = ArrivalAlert(Reason);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(Input, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Input));
            return RenderAgain();
        }

        ApiResult<TokenPair> api = await _handler.HandleAsync(Input, cancellationToken);
        if (!api.IsSuccess)
        {
            if (api.Errors != null)
            {
                AddBackendFieldErrors(api.Errors);
            }
            else
            {
                Alert = RefusalAlert(api.Problem!, Input.Email, CorrelationId.Get(HttpContext));
            }

            return RenderAgain();
        }

        await PortalSession.SignInAsync(HttpContext, api.Value!);
        if (SignInAddress.IsPortalPath(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl!);
        }

        return RedirectToPage("/Home/Home/Home");
    }

    // The password is never sent back to the browser.
    private PageResult RenderAgain()
    {
        Input.Password = "";
        return Page();
    }

    // Identity's field errors are shown with the page's own wording for that field.
    private void AddBackendFieldErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (string field in errors.Keys)
        {
            if (string.Equals(field, EmailField, StringComparison.OrdinalIgnoreCase))
            {
                string message = Messages.ValidationFormat("email");
                if (string.IsNullOrEmpty(Input.Email))
                {
                    message = Messages.ValidationRequired("Email");
                }
                ModelState.AddModelError("Input.Email", message);
            }
            else
            {
                ModelState.AddModelError("Input.Password", Messages.ValidationRequired("Password"));
            }
        }
    }

    private static PageAlert? ArrivalAlert(string? reason)
    {
        if (reason == SignInAddress.ReasonSessionEnded)
        {
            return PageAlert.Info(Messages.SessionEnded);
        }

        if (reason == SignInAddress.ReasonSignInRequired)
        {
            return PageAlert.Info(Messages.SessionSignInRequired);
        }

        return null;
    }

    private static PageAlert RefusalAlert(ApiProblem problem, string email, string correlationId)
    {
        switch (problem.Code)
        {
            case "INVALID_CREDENTIALS":
                return PageAlert.Danger(Messages.ProblemInvalidCredentials);
            case "EMAIL_NOT_VERIFIED":
                string resendHref = "/auth/resend-verification?email=" + Uri.EscapeDataString(email);
                return PageAlert.Warning("Please verify your email before signing in. Check your inbox for the verification link.")
                    .WithLink("Send the link again", resendHref);
            case "ACCOUNT_PENDING_APPROVAL":
                return PageAlert.Warning("Your operator application is waiting for review. We will email you when it is decided.");
            case "ACCOUNT_REJECTED":
                return PageAlert.Danger("Your operator application was not approved. The email we sent you gives the reason. You can apply again 7 days after the rejection.")
                    .WithLink("Re-apply", "/operator/reapply");
            case "ACCOUNT_SUSPENDED":
                return PageAlert.Danger(Messages.ProblemAccountSuspended);
            case "ACCOUNT_LOCKED":
                return LockedAlert(problem, correlationId);
            default:
                return SharedProblems.ToAlert(problem, correlationId);
        }
    }

    private static PageAlert LockedAlert(ApiProblem problem, string correlationId)
    {
        string? lockedUntil = IndiaTime.FormatIso(problem.GetExtension("lockedUntil"));
        if (lockedUntil is null)
        {
            return SharedProblems.ToAlert(problem, correlationId);
        }

        return PageAlert.Danger(Messages.ProblemAccountLocked(lockedUntil));
    }
}
