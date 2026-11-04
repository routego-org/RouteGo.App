namespace RouteGo.App.Features.Admin.Operators;

/// <summary>
/// The list's status filter, page and the confirmation asked for, all kept in the page address so
/// a reload or a shared link shows the same list (?status=rejected&amp;pageNumber=2&amp;reject={id}).
/// </summary>
public sealed class OperatorsQuery
{
    public const string WaitingStatus = "pendingApproval";

    /// <summary>pendingVerification, pendingApproval, approved or rejected; empty means the review queue.</summary>
    public string? Status { get; set; }

    /// <summary>The 1-based page of the list. Not named "page": Razor Pages reserves that route value for the page's own name.</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>The operator whose approval is being confirmed.</summary>
    public Guid? Approve { get; set; }

    /// <summary>The operator whose rejection is being confirmed.</summary>
    public Guid? Reject { get; set; }

    /// <summary>Whether the list shows the applications waiting for approval (the default view).</summary>
    public bool IsWaitingView()
    {
        return string.IsNullOrEmpty(Status) || Status == WaitingStatus;
    }
}

public sealed class DecisionInput   // mutable class: must round-trip on re-render
{
    public Guid OperatorId { get; set; }

    /// <summary>Shown in the confirmation again after a refused submit; never sent to Identity.</summary>
    public string CompanyName { get; set; } = "";

    public string Reason { get; set; } = "";
}

public sealed class OperatorsQueryValidator : AbstractValidator<OperatorsQuery>
{
    /// <summary>The application statuses, in the order the filter offers them.</summary>
    public static readonly string[] Statuses = new[] { "pendingApproval", "pendingVerification", "approved", "rejected" };

    // Identity's filter rule, checked before the search so a bad address shows a field message.
    public OperatorsQueryValidator()
    {
        RuleFor(x => x.Status).Must(status => Statuses.Contains(status)).When(x => !string.IsNullOrEmpty(x.Status))
            .WithMessage(Messages.ValidationFormat("status"));
    }
}

public sealed class RejectionValidator : AbstractValidator<DecisionInput>
{
    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 500;
    public const string ReasonLengthMessage = "Reason must be between 10 and 500 characters.";

    // Identity's reason rule, counted as Identity counts it (an emoji is one character).
    public RejectionValidator()
    {
        RuleFor(x => x.Reason).Must(IsAcceptableReason).WithMessage(ReasonLengthMessage);
    }

    private static bool IsAcceptableReason(string? reason)
    {
        if (reason is null)
        {
            return false;
        }

        int length = reason.EnumerateRunes().Count();
        return length >= MinReasonLength && length <= MaxReasonLength;
    }
}

/// <summary>Which decision the admin is confirming.</summary>
public enum Decision
{
    Approve,
    Reject,
}

/// <summary>The confirmation box: the decision, and the company it names.</summary>
public sealed record DecisionConfirmation(Decision Decision, Guid OperatorId, string CompanyName);

public sealed class OperatorsModel : PageModel
{
    private const string OperatorsPage = "/Admin/Operators/Operators";

    private readonly OperatorsHandler _handler;
    private readonly OperatorsQueryValidator _queryValidator = new OperatorsQueryValidator();
    private readonly RejectionValidator _rejectionValidator = new RejectionValidator();

    public OperatorsModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new OperatorsHandler(httpClientFactory);
    }

    [BindProperty(SupportsGet = true)]
    public OperatorsQuery Query { get; set; } = new OperatorsQuery();

    [BindProperty]
    public DecisionInput Input { get; set; } = new DecisionInput();

    /// <summary>The loaded list; null when it could not be loaded.</summary>
    public ApplicationsPage? Applications { get; private set; }

    /// <summary>The open confirmation, if the admin chose Approve or Reject.</summary>
    public DecisionConfirmation? Confirmation { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadListAsync(cancellationToken);
        Confirmation = ConfirmationFromList();
    }

    public async Task<IActionResult> OnPostApproveAsync(CancellationToken cancellationToken)
    {
        Confirmation = new DecisionConfirmation(Decision.Approve, Input.OperatorId, Input.CompanyName);
        ApiResult<ApplicationDecision> api = await _handler.ApproveAsync(Input.OperatorId, cancellationToken);
        return await FinishDecisionAsync(api, cancellationToken);
    }

    public async Task<IActionResult> OnPostRejectAsync(CancellationToken cancellationToken)
    {
        Confirmation = new DecisionConfirmation(Decision.Reject, Input.OperatorId, Input.CompanyName);
        ValidationResult validation = await _rejectionValidator.ValidateAsync(Input, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Input));
            return await RenderAgainAsync(cancellationToken);
        }

        ApiResult<ApplicationDecision> api = await _handler.RejectAsync(Input.OperatorId, Input.Reason, cancellationToken);
        return await FinishDecisionAsync(api, cancellationToken);
    }

    /// <summary>The list address with the current filter and the given page.</summary>
    public string ListAddress(int page)
    {
        return Url.Page(OperatorsPage, new { status = Query.Status, pageNumber = page })!;
    }

    /// <summary>The list address that opens the confirmation for one operator.</summary>
    public string ConfirmAddress(Decision decision, Guid operatorId)
    {
        if (decision == Decision.Approve)
        {
            return Url.Page(OperatorsPage, new { status = Query.Status, pageNumber = Query.PageNumber, approve = operatorId })!;
        }

        return Url.Page(OperatorsPage, new { status = Query.Status, pageNumber = Query.PageNumber, reject = operatorId })!;
    }

    /// <summary>Where the confirmation posts: the decision's handler, keeping the list's filter and page.</summary>
    public string DecideAddress(Decision decision)
    {
        string handler = decision.ToString();
        return Url.Page(OperatorsPage, handler, new { status = Query.Status, pageNumber = Query.PageNumber })!;
    }

    /// <summary>"Showing 21–40 of 45 applications" for the loaded page.</summary>
    public string ShowingText()
    {
        int from = ((Applications!.Page - 1) * OperatorsHandler.PageSize) + 1;
        int to = from + Applications.Items.Count - 1;
        return $"Showing {from}–{to} of {Applications.TotalCount} applications";
    }

    /// <summary>How many pages the matching applications fill.</summary>
    public int PageCount()
    {
        return (Applications!.TotalCount + OperatorsHandler.PageSize - 1) / OperatorsHandler.PageSize;
    }

    /// <summary>The status the filter shows as chosen: the review queue when none is in the address.</summary>
    public string ChosenStatus()
    {
        if (string.IsNullOrEmpty(Query.Status))
        {
            return OperatorsQuery.WaitingStatus;
        }

        return Query.Status;
    }

    /// <summary>Whether the admin may decide the application: only one waiting for approval.</summary>
    public static bool IsDecidable(ApplicationSummary application)
    {
        return application.Status == OperatorsQuery.WaitingStatus;
    }

    // A refusal because the application changed under the admin is told after the list is
    // reloaded, so the admin sees its current status; anything else keeps the confirmation.
    private async Task<IActionResult> FinishDecisionAsync(ApiResult<ApplicationDecision> api, CancellationToken cancellationToken)
    {
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = SuccessMessage(Confirmation!.Decision, api.Value!.CompanyName);
            return RedirectToList();
        }

        if (api.Errors != null)
        {
            ModelState.AddModelError("Input.Reason", RejectionValidator.ReasonLengthMessage);
            return await RenderAgainAsync(cancellationToken);
        }

        string? changedMessage = ChangedMeanwhileMessage(api.Problem!);
        if (changedMessage != null)
        {
            TempData[Flash.Key] = changedMessage;
            TempData[Flash.KindKey] = Flash.KindWarning;
            return RedirectToList();
        }

        Alert = SharedProblems.ToAlert(api.Problem!, CorrelationId.Get(HttpContext));
        return await RenderAgainAsync(cancellationToken);
    }

    private static string? ChangedMeanwhileMessage(ApiProblem problem)
    {
        string currentStatus = StateLabel(problem.GetExtension("currentStatus"));
        switch (problem.Code)
        {
            case "APPLICATION_NOT_PENDING":
                return $"This application is not waiting for review any more. It is now {currentStatus}. The list has been updated.";
            case "NOT_FOUND":
                return "We couldn't find that operator. It may have been removed.";
            default:
                return null;
        }
    }

    private static string SuccessMessage(Decision decision, string companyName)
    {
        if (decision == Decision.Approve)
        {
            return $"{companyName} is approved. We will email them, and they can sign in now.";
        }

        return $"{companyName} is rejected. We will email them the reason. They can re-apply after 7 days.";
    }

    private async Task<PageResult> RenderAgainAsync(CancellationToken cancellationToken)
    {
        await LoadListAsync(cancellationToken);
        return Page();
    }

    private RedirectToPageResult RedirectToList()
    {
        return RedirectToPage(OperatorsPage, new { status = Query.Status, pageNumber = Query.PageNumber });
    }

    private async Task LoadListAsync(CancellationToken cancellationToken)
    {
        ValidationResult validation = await _queryValidator.ValidateAsync(Query, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Query));
            return;
        }

        ApiResult<ApplicationsPage> api = await _handler.ListApplicationsAsync(Query, cancellationToken);
        if (api.IsSuccess)
        {
            Applications = api.Value;
            return;
        }

        if (api.Errors != null)
        {
            AddFilterErrors(api.Errors);
            return;
        }

        Alert = SharedProblems.ToAlert(api.Problem!, CorrelationId.Get(HttpContext));
    }

    // Identity named a filter it refused: the status shows the format message on its field.
    private void AddFilterErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (string field in errors.Keys)
        {
            if (field.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("Query.Status", Messages.ValidationFormat("status"));
                continue;
            }

            Alert = PageAlert.Danger(errors[field][0]);
        }
    }

    // The confirmation asked for in the address, shown only for an application on this page that
    // is still waiting; otherwise the list alone is shown.
    private DecisionConfirmation? ConfirmationFromList()
    {
        if (Applications is null)
        {
            return null;
        }

        foreach (ApplicationSummary application in Applications.Items)
        {
            if (!IsDecidable(application))
            {
                continue;
            }

            if (Query.Approve == application.OperatorId)
            {
                return new DecisionConfirmation(Decision.Approve, application.OperatorId, application.CompanyName);
            }

            if (Query.Reject == application.OperatorId)
            {
                return new DecisionConfirmation(Decision.Reject, application.OperatorId, application.CompanyName);
            }
        }

        return null;
    }

    /// <summary>The catalog's state.* words for an application status as Identity spells it.</summary>
    public static string StateLabel(string? status)
    {
        switch (status)
        {
            case "pendingVerification":
                return "Waiting for email verification";
            case "pendingApproval":
                return "Waiting for approval";
            case "approved":
                return "Approved";
            case "rejected":
                return "Rejected";
            default:
                return status ?? "";
        }
    }
}
