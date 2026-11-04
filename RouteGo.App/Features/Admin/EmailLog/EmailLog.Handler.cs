using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;

namespace RouteGo.App.Features.Admin.EmailLog;

/// <summary>One email attempt as Notification's send log records it; the message itself is never returned.</summary>
public sealed record SendLogEntry
{
    /// <summary>The entry's identifier, used to resend it.</summary>
    public required Guid SendLogEntryId { get; init; }

    /// <summary>The producing service's event ID; a resend keeps the original's value.</summary>
    public required string EventId { get; init; }

    /// <summary>customerVerification, operatorVerification, operatorApproved or operatorRejected.</summary>
    public required string EmailType { get; init; }

    /// <summary>The recipient; null only when the event named none.</summary>
    public required string? ToAddress { get; init; }

    /// <summary>The subject as sent; null when the email could not be built.</summary>
    public required string? Subject { get; init; }

    /// <summary>sending, sent or failed.</summary>
    public required string Status { get; init; }

    /// <summary>missingRecipient, invalidEventData, refusedByEmailService or emailServiceUnavailable; set only when failed.</summary>
    public required string? FailureReason { get; init; }

    public required string? FailureDetail { get; init; }

    /// <summary>The email service's receipt number; set only when sent.</summary>
    public required string? ProviderMessageId { get; init; }

    /// <summary>The entry this one resends; null for an email sent from an event.</summary>
    public required Guid? ResendOf { get; init; }

    public required string CorrelationId { get; init; }

    public required DateTimeOffset AttemptedAt { get; init; }

    public required DateTimeOffset? SentAt { get; init; }
}

/// <summary>One page of the emails tried to an address, newest first, with how many there are across all pages.</summary>
public sealed record SendLogPage(IReadOnlyList<SendLogEntry> Items, int TotalCount, int Page);

// The field of Notification's answer to a resend this page uses.
public sealed record ResentEmail(string? ToAddress);

public sealed class EmailLogHandler
{
    /// <summary>How many emails one page of the list shows.</summary>
    public const int PageSize = 20;

    private const string SendLogPath = "internal/sendLogEntries";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EmailLogHandler> _logger;

    public EmailLogHandler(IHttpClientFactory httpClientFactory, ILogger<EmailLogHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>One page of the emails Notification tried to send to the address (exact, ignoring case).</summary>
    public async Task<ApiResult<SendLogPage>> SearchAsync(string email, int page, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["toAddress"] = email,
            ["page"] = page.ToString(CultureInfo.InvariantCulture),
            ["pageSize"] = PageSize.ToString(CultureInfo.InvariantCulture),
        };
        string address = QueryHelpers.AddQueryString(SendLogPath, parameters);
        HttpClient notification = _httpClientFactory.CreateClient(HttpClients.Notification);
        HttpResponseMessage response = await notification.GetAsync(address, cancellationToken);
        return await response.ReadApiResultAsync<SendLogPage>(cancellationToken);
    }

    /// <summary>
    /// Sends one entry's email again. Notification cannot tell which admin asked, so every attempt is
    /// logged here with the admin's account identifier and the entry, whatever the answer.
    /// </summary>
    public async Task<ApiResult<ResentEmail>> ResendAsync(Guid sendLogEntryId, string adminId, CancellationToken cancellationToken)
    {
        HttpClient notification = _httpClientFactory.CreateClient(HttpClients.Notification);
        HttpResponseMessage response = await notification.PostAsync($"{SendLogPath}/{sendLogEntryId}/resend", null, cancellationToken);
        ApiResult<ResentEmail> result = await response.ReadApiResultAsync<ResentEmail>(cancellationToken);

        _logger.LogInformation(
            "Admin {AdminId} asked to resend send log entry {SendLogEntryId}: {ResendOutcome}",
            adminId, sendLogEntryId, Outcome(result));
        return result;
    }

    private static string Outcome(ApiResult<ResentEmail> result)
    {
        if (result.IsSuccess)
        {
            return "sent";
        }

        if (result.Problem is null)
        {
            return "VALIDATION_FAILED";
        }

        return result.Problem.Code ?? result.Problem.Status.ToString(CultureInfo.InvariantCulture);
    }
}
