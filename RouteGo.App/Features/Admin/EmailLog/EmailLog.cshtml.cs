using System.Security.Claims;
using DisplayFormatAttribute = System.ComponentModel.DataAnnotations.DisplayFormatAttribute;

namespace RouteGo.App.Features.Admin.EmailLog;

/// <summary>
/// The searched address, page and the confirmation asked for, all kept in the page address so a
/// reload shows the same list (?email=ravi@gmail.com&amp;pageNumber=2&amp;resend={id}).
/// </summary>
public sealed class EmailLogQuery
{
    /// <summary>Null before any search; empty when "Search" was chosen with nothing typed.</summary>
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? Email { get; set; }

    /// <summary>The 1-based page of the list. Not named "page": Razor Pages reserves that route value for the page's own name.</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>The entry whose resend is being confirmed.</summary>
    public Guid? Resend { get; set; }
}

public sealed class ResendInput   // mutable class: binds the confirmation's hidden field
{
    public Guid SendLogEntryId { get; set; }
}

public sealed class EmailLogQueryValidator : AbstractValidator<EmailLogQuery>
{
    public const int MaxEmailLength = 254;

    // Notification's address rule, checked before the search so a bad address shows a field message.
    public EmailLogQueryValidator()
    {
        RuleFor(x => x.Email).Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(Messages.ValidationRequired("Email address"))
            .MaximumLength(MaxEmailLength).WithMessage(Messages.ValidationFormat("email address"))
            .EmailAddress().WithMessage(Messages.ValidationFormat("email address"));
    }
}

public sealed class EmailLogModel : PageModel
{
    private const string EmailLogPage = "/Admin/EmailLog/EmailLog";
    private const string KeyProblemMessage =
        "The email log is not set up correctly: the portal cannot connect to the Notification service. Tell the platform engineer.";

    private readonly EmailLogHandler _handler;
    private readonly EmailLogQueryValidator _queryValidator = new EmailLogQueryValidator();

    public EmailLogModel(IHttpClientFactory httpClientFactory, ILogger<EmailLogHandler> logger)
    {
        _handler = new EmailLogHandler(httpClientFactory, logger);
    }

    [BindProperty(SupportsGet = true)]
    public EmailLogQuery Query { get; set; } = new EmailLogQuery();

    [BindProperty]
    public ResendInput Input { get; set; } = new ResendInput();

    /// <summary>The loaded list; null before a search or when it could not be loaded.</summary>
    public SendLogPage? Emails { get; private set; }

    /// <summary>The entry whose resend the admin is confirming, if they chose "Resend".</summary>
    public SendLogEntry? Confirmation { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadListAsync(cancellationToken);
        Confirmation = ConfirmationFromList();
    }

    public async Task<IActionResult> OnPostResendAsync(CancellationToken cancellationToken)
    {
        string adminId = User.FindFirstValue(PortalSession.UserIdClaim)!;
        ApiResult<ResentEmail> api = await _handler.ResendAsync(Input.SendLogEntryId, adminId, cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = $"The email was sent again to {api.Value!.ToAddress}.";
            return RedirectToPage(EmailLogPage, new { email = Query.Email, pageNumber = Query.PageNumber });
        }

        // Notification records a failed resend as a new entry too, so the list is always reloaded.
        Alert = ResendRefusal(api);
        await LoadListAsync(cancellationToken);
        return Page();
    }

    /// <summary>The list address for the searched email and the given page.</summary>
    public string ListAddress(int page)
    {
        return Url.Page(EmailLogPage, new { email = Query.Email, pageNumber = page })!;
    }

    /// <summary>The list address that opens the resend confirmation for one entry.</summary>
    public string ConfirmAddress(Guid sendLogEntryId)
    {
        return Url.Page(EmailLogPage, new { email = Query.Email, pageNumber = Query.PageNumber, resend = sendLogEntryId })!;
    }

    /// <summary>Where the confirmation posts, keeping the searched email and page.</summary>
    public string ResendAddress()
    {
        return Url.Page(EmailLogPage, "Resend", new { email = Query.Email, pageNumber = Query.PageNumber })!;
    }

    /// <summary>"Showing 21–40 of 45 emails" for the loaded page.</summary>
    public string ShowingText()
    {
        int from = ((Emails!.Page - 1) * EmailLogHandler.PageSize) + 1;
        int to = from + Emails.Items.Count - 1;
        return $"Showing {from}–{to} of {Emails.TotalCount} emails";
    }

    /// <summary>How many pages the emails to the address fill.</summary>
    public int PageCount()
    {
        return (Emails!.TotalCount + EmailLogHandler.PageSize - 1) / EmailLogHandler.PageSize;
    }

    /// <summary>
    /// Whether "Resend" is offered: every entry except one that failed because it had no recipient or
    /// unusable event data — there is nothing to send. Link expiry is Notification's to decide.
    /// </summary>
    public static bool CanResend(SendLogEntry entry)
    {
        return entry.FailureReason != "missingRecipient" && entry.FailureReason != "invalidEventData";
    }

    private async Task LoadListAsync(CancellationToken cancellationToken)
    {
        if (Query.Email is null)
        {
            return;
        }

        ValidationResult validation = await _queryValidator.ValidateAsync(Query, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Query));
            return;
        }

        ApiResult<SendLogPage> api = await _handler.SearchAsync(Query.Email, Query.PageNumber, cancellationToken);
        if (api.IsSuccess)
        {
            Emails = api.Value;
            return;
        }

        if (api.Errors != null)
        {
            ModelState.AddModelError("Query.Email", Messages.ValidationFormat("email address"));
            return;
        }

        Alert = SearchRefusal(api.Problem!);
    }

    private PageAlert SearchRefusal(ApiProblem problem)
    {
        if (problem.Code == "UNAUTHORIZED")
        {
            return PageAlert.Danger(KeyProblemMessage);
        }

        return SharedProblems.ToAlert(problem, CorrelationId.Get(HttpContext));
    }

    private PageAlert ResendRefusal(ApiResult<ResentEmail> api)
    {
        if (api.Problem is null)
        {
            return PageAlert.Danger(Messages.ProblemInternalError(CorrelationId.Get(HttpContext)));
        }

        string? message = ResendRefusalMessage(api.Problem);
        if (message != null)
        {
            return PageAlert.Danger(message);
        }

        return SharedProblems.ToAlert(api.Problem, CorrelationId.Get(HttpContext));
    }

    // Notification's own outage answer names the failed entry it recorded; the portal's answer for
    // an unreachable Notification does not, and falls through to the shared "not available" message.
    private static string? ResendRefusalMessage(ApiProblem problem)
    {
        bool isEmailServiceDown = problem.Code == SharedProblems.UpstreamUnavailable && problem.GetExtension("sendLogEntryId") != null;
        if (isEmailServiceDown)
        {
            return "The email service is not available, so the email was not sent. The failed try is now in the list. Try again later.";
        }

        if (problem.Code == "RESEND_LINK_EXPIRED")
        {
            string? expiredAt = IndiaTime.FormatIso(problem.GetExtension("linkExpiredAt"));
            return $"This email cannot be resent because its verification link expired on {expiredAt}. Ask the person to use 'Send the verification email again' on the website.";
        }

        switch (problem.Code)
        {
            case "RESEND_NOT_POSSIBLE":
                return $"This email cannot be resent: {FailureReasonLabel(problem.GetExtension("failureReason"))}.";
            case "EMAIL_REFUSED":
                return $"The email service refused this email: {problem.GetExtension("providerReason")} The failed try is now in the list.";
            case "NOT_FOUND":
                return "We couldn't find that email. It may have been removed.";
            case "UNAUTHORIZED":
                return KeyProblemMessage;
            default:
                return null;
        }
    }

    // The confirmation asked for in the address, shown only for an entry on this page that may be resent.
    private SendLogEntry? ConfirmationFromList()
    {
        if (Emails is null || Query.Resend is null)
        {
            return null;
        }

        foreach (SendLogEntry entry in Emails.Items)
        {
            if (entry.SendLogEntryId == Query.Resend && CanResend(entry))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>The catalog's email_type.* words.</summary>
    public static string EmailTypeLabel(string emailType)
    {
        switch (emailType)
        {
            case "customerVerification":
                return "Customer verification";
            case "operatorVerification":
                return "Operator verification";
            case "operatorApproved":
                return "Operator approved";
            case "operatorRejected":
                return "Operator rejected";
            default:
                return emailType;
        }
    }

    /// <summary>The catalog's state.sending / state.sent / state.failed words.</summary>
    public static string StateLabel(string status)
    {
        switch (status)
        {
            case "sending":
                return "Sending";
            case "sent":
                return "Sent";
            case "failed":
                return "Failed";
            default:
                return status;
        }
    }

    /// <summary>The catalog's failure_reason.* words.</summary>
    public static string FailureReasonLabel(string? failureReason)
    {
        switch (failureReason)
        {
            case "missingRecipient":
                return "No email address";
            case "invalidEventData":
                return "The event data was unusable";
            case "refusedByEmailService":
                return "Refused by the email service";
            case "emailServiceUnavailable":
                return "Email service unavailable";
            default:
                return failureReason ?? "";
        }
    }
}
