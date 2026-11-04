using System.Globalization;

namespace RouteGo.App.Features.Auth.ResendVerification;

public sealed class ResendVerificationInput   // mutable class: must round-trip on re-render
{
    public string Email { get; set; } = "";
}

public sealed class ResendVerificationValidator : AbstractValidator<ResendVerificationInput>
{
    public ResendVerificationValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Email"))
            .EmailAddress().WithMessage(Messages.ValidationFormat("email"));
    }
}

public sealed record ResendVerificationRequest(string Email);

// Identity's VerificationEmailSent.
public sealed record ResendVerificationResponse(string Email, DateTimeOffset LinkExpiresAt);

public sealed class ResendVerificationHandler
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ResendVerificationHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Asks Identity to send a brand-new verification link to the email.</summary>
    public async Task<ApiResult<ResendVerificationResponse>> HandleAsync(
        ResendVerificationInput input, CancellationToken cancellationToken)
    {
        HttpClient identity = _httpClientFactory.CreateClient(HttpClients.Identity);
        var request = new ResendVerificationRequest(input.Email);
        HttpResponseMessage response = await identity.PostAsJsonAsync("auth/verificationEmails", request, WireJson.Options, cancellationToken);
        return await response.ReadApiResultAsync<ResendVerificationResponse>(cancellationToken);
    }
}

public sealed class ResendVerificationModel : PageModel
{
    private const string SentMessageKey = "ResendVerification.Sent";
    private const int SecondsPerMinute = 60;
    private const int MinutesPerHour = 60;

    private readonly ResendVerificationHandler _handler;
    private readonly ResendVerificationValidator _validator = new ResendVerificationValidator();

    public ResendVerificationModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new ResendVerificationHandler(httpClientFactory);
    }

    [BindProperty]
    public ResendVerificationInput Input { get; set; } = new ResendVerificationInput();

    /// <summary>Set after a successful resend: the confirmation that replaces the form.</summary>
    public string? SentMessage { get; private set; }

    public PageAlert? Alert { get; private set; }

    /// <summary>How long the send action stays unavailable after Identity said "asked too often".</summary>
    public int WaitSeconds { get; private set; }

    public void OnGet([FromQuery(Name = "email")] string? email)
    {
        SentMessage = TempData[SentMessageKey] as string;
        if (!string.IsNullOrEmpty(email))
        {
            Input.Email = email;
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(Input, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Input));
            return Page();
        }

        ApiResult<ResendVerificationResponse> api = await _handler.HandleAsync(Input, cancellationToken);
        if (!api.IsSuccess)
        {
            if (api.Errors != null)
            {
                ModelState.AddModelError("Input.Email", Messages.ValidationFormat("email"));
                return Page();
            }

            Alert = RefusalAlert(api.Problem!, CorrelationId.Get(HttpContext));
            return Page();
        }

        ResendVerificationResponse sent = api.Value!;
        string expiresAt = IndiaTime.Format(sent.LinkExpiresAt);
        TempData[SentMessageKey] =
            $"We sent a new verification link to {sent.Email}. It works until {expiresAt}. Links in older emails no longer work.";
        return RedirectToPage("/Auth/ResendVerification/ResendVerification");
    }

    private PageAlert RefusalAlert(ApiProblem problem, string correlationId)
    {
        switch (problem.Code)
        {
            case "EMAIL_NOT_REGISTERED":
                return PageAlert.Warning("No account uses this email. Check the spelling, or create an account.")
                    .WithLink("Create an account", "/auth/register");
            case "EMAIL_ALREADY_VERIFIED":
                return AlreadyVerifiedAlert(problem.GetExtension("accountStatus"));
            case "RATE_LIMITED":
                return RateLimitedAlert(problem, correlationId);
            default:
                return SharedProblems.ToAlert(problem, correlationId);
        }
    }

    private static PageAlert AlreadyVerifiedAlert(string? accountStatus)
    {
        if (accountStatus == "pendingApproval")
        {
            return PageAlert.Info("This email is already verified. Your operator application is waiting for review.");
        }

        return PageAlert.Info("This email is already verified. You can sign in.").WithLink("Sign in", "/auth/login");
    }

    // The wait is Identity's; the page shows it and never computes its own.
    private PageAlert RateLimitedAlert(ApiProblem problem, string correlationId)
    {
        string? retryAfter = problem.GetExtension("retryAfterSeconds");
        if (!int.TryParse(retryAfter, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
        {
            return SharedProblems.ToAlert(problem, correlationId);
        }

        WaitSeconds = seconds;
        return PageAlert.Warning($"You asked for a new link a short time ago. You can ask again in {WaitInWords(seconds)}.");
    }

    // The design system's waiting-time words: "30 seconds", "5 minutes", "19 hours 40 minutes".
    private static string WaitInWords(int seconds)
    {
        if (seconds < SecondsPerMinute)
        {
            return Count(seconds, "second");
        }

        int totalMinutes = (seconds + SecondsPerMinute - 1) / SecondsPerMinute;
        int hours = totalMinutes / MinutesPerHour;
        int minutes = totalMinutes % MinutesPerHour;
        if (hours == 0)
        {
            return Count(minutes, "minute");
        }

        if (minutes == 0)
        {
            return Count(hours, "hour");
        }

        return Count(hours, "hour") + " " + Count(minutes, "minute");
    }

    private static string Count(int amount, string unit)
    {
        if (amount == 1)
        {
            return "1 " + unit;
        }

        return amount.ToString(CultureInfo.InvariantCulture) + " " + unit + "s";
    }
}
