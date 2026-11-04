namespace RouteGo.App.Features.Admin.Users;

/// <summary>
/// The list's filters, page and the confirmation asked for, all kept in the page address so a
/// reload or a shared link shows the same list (?email=alic&amp;role=customer&amp;pageNumber=2&amp;suspend={id}).
/// </summary>
public sealed class UsersQuery
{
    public string? Email { get; set; }

    /// <summary>customer, operator or admin, as Identity spells them.</summary>
    public string? Role { get; set; }

    /// <summary>pendingVerification, pendingApproval, active, rejected or suspended.</summary>
    public string? Status { get; set; }

    /// <summary>The 1-based page of the list. Not named "page": Razor Pages reserves that route value for the page's own name.</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>The account whose suspension is being confirmed.</summary>
    public Guid? Suspend { get; set; }

    /// <summary>The account whose reactivation is being confirmed.</summary>
    public Guid? Reactivate { get; set; }

    public bool HasFilter()
    {
        return !string.IsNullOrEmpty(Email) || !string.IsNullOrEmpty(Role) || !string.IsNullOrEmpty(Status);
    }
}

public sealed class UserActionInput   // mutable class: must round-trip on re-render
{
    public Guid UserId { get; set; }

    /// <summary>Shown in the confirmation again after a refused submit; never sent to Identity.</summary>
    public string Email { get; set; } = "";

    public string Reason { get; set; } = "";
}

public sealed class UsersQueryValidator : AbstractValidator<UsersQuery>
{
    public const int MaxEmailLength = 254;

    private static readonly string[] _roles = new[] { "customer", "operator", "admin" };
    private static readonly string[] _statuses =
        new[] { "pendingVerification", "pendingApproval", "active", "rejected", "suspended" };

    // Identity's filter rules, checked before the search so a bad address shows a field message.
    public UsersQueryValidator()
    {
        RuleFor(x => x.Email).MaximumLength(MaxEmailLength).WithMessage(Messages.ValidationFormat("email"));
        RuleFor(x => x.Role).Must(role => _roles.Contains(role)).When(x => !string.IsNullOrEmpty(x.Role))
            .WithMessage(Messages.ValidationFormat("role"));
        RuleFor(x => x.Status).Must(status => _statuses.Contains(status)).When(x => !string.IsNullOrEmpty(x.Status))
            .WithMessage(Messages.ValidationFormat("status"));
    }
}

public sealed class UserActionValidator : AbstractValidator<UserActionInput>
{
    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 500;
    public const string ReasonLengthMessage = "Reason must be between 10 and 500 characters.";

    // Identity's reason rule, counted as Identity counts it (an emoji is one character).
    public UserActionValidator()
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

/// <summary>Which account change the admin is confirming.</summary>
public enum UserAction
{
    Suspend,
    Reactivate,
}

/// <summary>The confirmation box: the change, and the account it names.</summary>
public sealed record UserActionConfirmation(UserAction Action, Guid UserId, string Email);

public sealed class UsersModel : PageModel
{
    private const string UsersPage = "/Admin/Users/Users";

    private readonly UsersHandler _handler;
    private readonly UsersQueryValidator _queryValidator = new UsersQueryValidator();
    private readonly UserActionValidator _actionValidator = new UserActionValidator();

    public UsersModel(IHttpClientFactory httpClientFactory)
    {
        _handler = new UsersHandler(httpClientFactory);
    }

    [BindProperty(SupportsGet = true)]
    public UsersQuery Query { get; set; } = new UsersQuery();

    [BindProperty]
    public UserActionInput Input { get; set; } = new UserActionInput();

    /// <summary>The loaded list; null when it could not be loaded.</summary>
    public UsersPage? Users { get; private set; }

    /// <summary>The open confirmation, if the admin chose Suspend or Reactivate.</summary>
    public UserActionConfirmation? Confirmation { get; private set; }

    public PageAlert? Alert { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadListAsync(cancellationToken);
        Confirmation = ConfirmationFromList();
    }

    public async Task<IActionResult> OnPostSuspendAsync(CancellationToken cancellationToken)
    {
        return await ChangeAccountAsync(UserAction.Suspend, cancellationToken);
    }

    public async Task<IActionResult> OnPostReactivateAsync(CancellationToken cancellationToken)
    {
        return await ChangeAccountAsync(UserAction.Reactivate, cancellationToken);
    }

    /// <summary>The list address with the current filters and the given page.</summary>
    public string ListAddress(int page)
    {
        return Url.Page(UsersPage, new { email = Query.Email, role = Query.Role, status = Query.Status, pageNumber = page })!;
    }

    /// <summary>The list address that opens the confirmation for one account.</summary>
    public string ConfirmAddress(UserAction action, Guid userId)
    {
        if (action == UserAction.Suspend)
        {
            return Url.Page(UsersPage, new { email = Query.Email, role = Query.Role, status = Query.Status, pageNumber = Query.PageNumber, suspend = userId })!;
        }

        return Url.Page(UsersPage, new { email = Query.Email, role = Query.Role, status = Query.Status, pageNumber = Query.PageNumber, reactivate = userId })!;
    }

    /// <summary>Where the confirmation posts: the change's handler, keeping the list's filters and page.</summary>
    public string ChangeAddress(UserAction action)
    {
        string handler = action.ToString();
        return Url.Page(UsersPage, handler, new { email = Query.Email, role = Query.Role, status = Query.Status, pageNumber = Query.PageNumber })!;
    }

    /// <summary>"Showing 21–40 of 45 accounts" for the loaded page.</summary>
    public string ShowingText()
    {
        int from = ((Users!.Page - 1) * UsersHandler.PageSize) + 1;
        int to = from + Users.Items.Count - 1;
        return $"Showing {from}–{to} of {Users.TotalCount} accounts";
    }

    /// <summary>How many pages the matching accounts fill.</summary>
    public int PageCount()
    {
        return (Users!.TotalCount + UsersHandler.PageSize - 1) / UsersHandler.PageSize;
    }

    /// <summary>Whether the admin may suspend the account: an active customer or operator.</summary>
    public static bool CanSuspend(UserSummary user)
    {
        return user.Status == "active" && (user.Role == "customer" || user.Role == "operator");
    }

    /// <summary>Whether the admin may reactivate the account: a suspended one.</summary>
    public static bool CanReactivate(UserSummary user)
    {
        return user.Status == "suspended";
    }

    private async Task<IActionResult> ChangeAccountAsync(UserAction action, CancellationToken cancellationToken)
    {
        Confirmation = new UserActionConfirmation(action, Input.UserId, Input.Email);
        ValidationResult validation = await _actionValidator.ValidateAsync(Input, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Input));
            return await RenderAgainAsync(cancellationToken);
        }

        ApiResult<AccountChange> api = await SendChangeAsync(action, cancellationToken);
        if (api.IsSuccess)
        {
            TempData[Flash.Key] = SuccessMessage(action, api.Value!.Email);
            return RedirectToList();
        }

        return await ShowRefusalAsync(api, cancellationToken);
    }

    private async Task<ApiResult<AccountChange>> SendChangeAsync(UserAction action, CancellationToken cancellationToken)
    {
        if (action == UserAction.Suspend)
        {
            return await _handler.SuspendAsync(Input.UserId, Input.Reason, cancellationToken);
        }

        return await _handler.ReactivateAsync(Input.UserId, Input.Reason, cancellationToken);
    }

    // A refusal because the account changed under the admin is told after the list is reloaded,
    // so the admin sees its current status; anything else keeps the confirmation and the reason.
    private async Task<IActionResult> ShowRefusalAsync(ApiResult<AccountChange> api, CancellationToken cancellationToken)
    {
        if (api.Errors != null)
        {
            ModelState.AddModelError("Input.Reason", UserActionValidator.ReasonLengthMessage);
            return await RenderAgainAsync(cancellationToken);
        }

        string? changedMessage = AccountChangedMessage(api.Problem!);
        if (changedMessage != null)
        {
            TempData[Flash.Key] = changedMessage;
            TempData[Flash.KindKey] = Flash.KindWarning;
            return RedirectToList();
        }

        Alert = SharedProblems.ToAlert(api.Problem!, CorrelationId.Get(HttpContext));
        return await RenderAgainAsync(cancellationToken);
    }

    private static string? AccountChangedMessage(ApiProblem problem)
    {
        string currentStatus = StateLabel(problem.GetExtension("currentStatus"));
        switch (problem.Code)
        {
            case "USER_NOT_ACTIVE":
                return $"This account is no longer active. It is now {currentStatus}. The list has been updated.";
            case "USER_NOT_SUSPENDED":
                return $"This account is not suspended. It is now {currentStatus}. The list has been updated.";
            case "ADMIN_CANNOT_BE_SUSPENDED":
                return "Admin accounts cannot be suspended.";
            case "NOT_FOUND":
                return "We couldn't find that account. It may have been removed.";
            default:
                return null;
        }
    }

    private static string SuccessMessage(UserAction action, string email)
    {
        if (action == UserAction.Suspend)
        {
            return $"{email} is suspended. They will be signed out within 15 minutes.";
        }

        return $"{email} is active again. They can sign in now.";
    }

    private async Task<PageResult> RenderAgainAsync(CancellationToken cancellationToken)
    {
        await LoadListAsync(cancellationToken);
        return Page();
    }

    private RedirectToPageResult RedirectToList()
    {
        return RedirectToPage(UsersPage, new { email = Query.Email, role = Query.Role, status = Query.Status, pageNumber = Query.PageNumber });
    }

    private async Task LoadListAsync(CancellationToken cancellationToken)
    {
        ValidationResult validation = await _queryValidator.ValidateAsync(Query, cancellationToken);
        if (!validation.IsValid)
        {
            validation.AddToModelState(ModelState, nameof(Query));
            return;
        }

        ApiResult<UsersPage> api = await _handler.ListUsersAsync(Query, cancellationToken);
        if (api.IsSuccess)
        {
            Users = api.Value;
            return;
        }

        if (api.Errors != null)
        {
            AddFilterErrors(api.Errors);
            return;
        }

        Alert = SharedProblems.ToAlert(api.Problem!, CorrelationId.Get(HttpContext));
    }

    // Identity named a filter it refused; each shows the format message on its own field.
    private void AddFilterErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (string field in errors.Keys)
        {
            string name = field.ToLowerInvariant();
            if (name == "email" || name == "role" || name == "status")
            {
                ModelState.AddModelError("Query." + field, Messages.ValidationFormat(name));
                continue;
            }

            Alert = PageAlert.Danger(errors[field][0]);
        }
    }

    // The confirmation asked for in the address, shown only for an account on this page that
    // still allows the change; otherwise the list alone is shown.
    private UserActionConfirmation? ConfirmationFromList()
    {
        if (Users is null)
        {
            return null;
        }

        foreach (UserSummary user in Users.Items)
        {
            if (Query.Suspend == user.UserId && CanSuspend(user))
            {
                return new UserActionConfirmation(UserAction.Suspend, user.UserId, user.Email);
            }

            if (Query.Reactivate == user.UserId && CanReactivate(user))
            {
                return new UserActionConfirmation(UserAction.Reactivate, user.UserId, user.Email);
            }
        }

        return null;
    }

    /// <summary>The catalog's state.* words for an account status as Identity spells it.</summary>
    public static string StateLabel(string? status)
    {
        switch (status)
        {
            case "pendingVerification":
                return "Waiting for email verification";
            case "pendingApproval":
                return "Waiting for approval";
            case "active":
                return "Active";
            case "rejected":
                return "Rejected";
            case "suspended":
                return "Suspended";
            default:
                return status ?? "";
        }
    }
}
