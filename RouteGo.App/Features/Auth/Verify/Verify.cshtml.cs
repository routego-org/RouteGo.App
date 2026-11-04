namespace RouteGo.App.Features.Auth.Verify;

public sealed class VerifyInput   // mutable class: must round-trip on re-render
{
    /// <summary>The verification token from the link, sent to Identity unchanged.</summary>
    public string Token { get; set; } = "";
}

public sealed class VerifyValidator : AbstractValidator<VerifyInput>
{
    public VerifyValidator()
    {
        // A link without its token is incomplete; the page says so instead of calling Identity.
        RuleFor(x => x.Token).NotEmpty();
    }
}

public sealed record VerifyRequest(string Token);

// Identity's EmailVerificationResult: a customer gets a token pair, an operator gets none.
public sealed record VerifyResponse(string Status, TokenPair? Tokens);

public sealed class VerifyHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public VerifyHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Uses the verification link: activates a customer, or sends an operator to review.</summary>
    public async Task<ApiResult<VerifyResponse>> HandleAsync(VerifyInput input, CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var request = new VerifyRequest(input.Token);
        HttpResponseMessage response = await identity.PostAsJsonAsync("auth/emailVerifications", request, WireJson.Options, cancellationToken);
        return await response.ReadApiResultAsync<VerifyResponse>(cancellationToken);
    }
}

public sealed class VerifyModel : PageModel
{
    private const string OutcomeKey = "Verify.Outcome";
    private const string OutcomePendingApproval = "pendingApproval";
    private const string StatusActive = "active";
    private const string ResendPath = "/auth/resend-verification";

    private readonly VerifyHandler _handler;
    private readonly VerifyValidator _validator = new VerifyValidator();

    public VerifyModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new VerifyHandler(httpClientFactory);
    }

    [BindProperty]
    public VerifyInput Input { get; set; } = new VerifyInput();

    /// <summary>Whether the "Verify my email" action is offered.</summary>
    public bool CanVerify { get; private set; }

    public PageAlert? Alert { get; private set; }

    // Opening the page never calls Identity: email link checkers open links but do not submit
    // forms, so only the person's own click uses the single-use link.
    public void OnGet([FromQuery(Name = "token")] string? token)
    {
        if (TempData[OutcomeKey] as string == OutcomePendingApproval)
        {
            Alert = PageAlert.Success("Your email is verified. Our team will now review your operator application. We will email you when it is decided.")
                .WithLink(Messages.AccessGoHome, "/");
            return;
        }

        if (string.IsNullOrEmpty(token))
        {
            Alert = IncompleteAlert();
            return;
        }

        Input.Token = token;
        CanVerify = true;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(Input, cancellationToken);
        if (!validation.IsValid)
        {
            Alert = IncompleteAlert();
            return Page();
        }

        ApiResult<VerifyResponse> api = await _handler.HandleAsync(Input, cancellationToken);
        if (!api.IsSuccess)
        {
            Alert = RefusalAlert(api, CorrelationId.Get(HttpContext));
            CanVerify = IsWorthRetrying(api);
            return Page();
        }

        VerifyResponse result = api.Value!;
        if (result.Status == StatusActive && result.Tokens != null)
        {
            await PortalSession.SignInAsync(HttpContext, result.Tokens);
            TempData[Flash.Key] = "Your email is verified. Welcome to RouteGo.";
            return RedirectToPage("/Home/Home/Home");
        }

        TempData[OutcomeKey] = OutcomePendingApproval;
        return RedirectToPage("/Auth/Verify/Verify");
    }

    private static PageAlert IncompleteAlert()
    {
        return PageAlert.Warning("This verification link is not complete. Open the link from your email again, or ask for a new one.")
            .WithLink("Send me a new link", ResendPath);
    }

    private static PageAlert RefusalAlert(ApiResult<VerifyResponse> api, string correlationId)
    {
        if (api.Errors != null)
        {
            return IncompleteAlert();
        }

        ApiProblem problem = api.Problem!;
        switch (problem.Code)
        {
            case "VERIFICATION_LINK_EXPIRED":
                return PageAlert.Warning("This verification link has expired.").WithLink("Send me a new link", ResendPath);
            case "VERIFICATION_LINK_ALREADY_USED":
                return AlreadyUsedAlert(problem.GetExtension("accountStatus"));
            case "VERIFICATION_LINK_INVALID":
                return PageAlert.Warning("This verification link is not valid. It may have been replaced by a newer link. Use the link in the newest email, or ask for a new one.")
                    .WithLink("Send me a new link", ResendPath);
            default:
                return SharedProblems.ToAlert(problem, correlationId);
        }
    }

    private static PageAlert AlreadyUsedAlert(string? accountStatus)
    {
        if (accountStatus == OutcomePendingApproval)
        {
            return PageAlert.Info("Your email is already verified. Your operator application is waiting for review.")
                .WithLink(Messages.AccessGoHome, "/");
        }

        return PageAlert.Info("Your email is already verified. You can sign in.").WithLink("Sign in", "/auth/login");
    }

    // An outage or a server error leaves the link unused, so the person may try again; every other
    // answer is final for this link.
    private static bool IsWorthRetrying(ApiResult<VerifyResponse> api)
    {
        if (api.Problem is null)
        {
            return false;
        }

        string? code = api.Problem.Code;
        bool isKnownRefusal = code == "VERIFICATION_LINK_EXPIRED"
            || code == "VERIFICATION_LINK_ALREADY_USED"
            || code == "VERIFICATION_LINK_INVALID";
        return !isKnownRefusal;
    }
}
