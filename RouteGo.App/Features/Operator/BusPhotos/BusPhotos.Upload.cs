namespace RouteGo.App.Features.Operator.BusPhotos;

/// <summary>
/// What the page script does next with one file, as the portal answers it: send the file to the
/// slot, ask again, or show the file's result. The words are made here, so the script only shows them.
/// </summary>
public sealed record UploadStep
{
    /// <summary>One of <see cref="UploadOutcome"/>'s values.</summary>
    public required string Outcome { get; init; }

    /// <summary>The file's result line; null while there is still a step to take.</summary>
    public string? Message { get; init; }

    /// <summary>Whether no further file may be sent (the account cannot add photos right now).</summary>
    public bool Stops { get; init; }

    public Guid? PhotoId { get; init; }

    public string? UploadUrl { get; init; }

    /// <summary>The HTTP method the browser sends the file with, upper case.</summary>
    public string? UploadMethod { get; init; }

    public string? ContentType { get; init; }
}

/// <summary>The steps the page script knows.</summary>
public static class UploadOutcome
{
    public const string Send = "send";
    public const string AskAgain = "askAgain";
    public const string Added = "added";
    public const string Failed = "failed";
}

/// <summary>The answer to each upload step, from Fleet's answer, in the spec's words.</summary>
public static class UploadAnswers
{
    public const string PhotoNotUploaded = "PHOTO_NOT_UPLOADED";

    private const string PhotoLimitReached = "PHOTO_LIMIT_REACHED";
    private const string PhotoUploadExpired = "PHOTO_UPLOAD_EXPIRED";
    private const string PhotoInvalid = "PHOTO_INVALID";
    private const string ValidationFailed = "VALIDATION_FAILED";

    public const string TooLarge = "the file is larger than 3 MB.";
    public const string WrongType = "only JPEG and PNG photos can be used.";
    public const string NotSent = "the file could not be sent. Check your connection and try again.";
    public const string ServerError = "something went wrong on our side.";

    private const string TypeMismatch = "the file is not the type its name says. Save it again as JPEG or PNG and try again.";
    private const string TooSlow = "sending the photo took too long. Please try again.";
    private const string LimitReached = "this bus already has 5 photos, or other photos are still being added. Wait 5 minutes or delete a photo.";
    private const string Refused = "only JPEG and PNG photos up to 3 MB can be used.";
    private const string Unavailable = "this part of RouteGo is not available right now. Please try again in a few minutes.";

    /// <summary>"{file_name} — not added: {reason}".</summary>
    public static string NotAdded(string fileName, string reason)
    {
        return $"{fileName} — not added: {reason}";
    }

    public static UploadStep Send(PhotoUploadSlot slot)
    {
        return new UploadStep
        {
            Outcome = UploadOutcome.Send,
            PhotoId = slot.PhotoId,
            UploadUrl = slot.UploadUrl,
            UploadMethod = slot.UploadMethod.ToUpperInvariant(),
            ContentType = slot.ContentType,
        };
    }

    public static UploadStep Added(string fileName)
    {
        return new UploadStep { Outcome = UploadOutcome.Added, Message = $"{fileName} — added" };
    }

    public static UploadStep AskAgain()
    {
        return new UploadStep { Outcome = UploadOutcome.AskAgain };
    }

    public static UploadStep Failed(string fileName, string reason)
    {
        return new UploadStep { Outcome = UploadOutcome.Failed, Message = NotAdded(fileName, reason) };
    }

    /// <summary>Why Fleet gave no upload slot.</summary>
    public static UploadStep ForSlotRefusal(string fileName, ApiResult<PhotoUploadSlot> api)
    {
        if (api.Errors != null)
        {
            return Failed(fileName, Refused);
        }

        ApiProblem problem = api.Problem!;
        if (problem.Code == PhotoLimitReached)
        {
            return Failed(fileName, LimitReached);
        }

        if (problem.Code == ValidationFailed)
        {
            return Failed(fileName, Refused);
        }

        if (problem.Code == FleetDisplay.NotFound)
        {
            return Stopped(Messages.ProblemNotFound("bus"));
        }

        return ForAnyRefusal(fileName, problem);
    }

    /// <summary>Why Fleet did not add the stored file. PHOTO_NOT_UPLOADED is the page model's to decide.</summary>
    public static UploadStep ForConfirmRefusal(string fileName, ApiProblem problem)
    {
        if (problem.Code == PhotoUploadExpired)
        {
            return Failed(fileName, TooSlow);
        }

        if (problem.Code == PhotoInvalid)
        {
            return Failed(fileName, InvalidReason(problem.GetExtension("reason")));
        }

        return ForAnyRefusal(fileName, problem);
    }

    private static UploadStep ForAnyRefusal(string fileName, ApiProblem problem)
    {
        if (problem.Code == FleetDisplay.OperatorProfileNotReady)
        {
            return Stopped(Messages.ProblemOperatorProfileNotReady);
        }

        if (problem.Code == SharedProblems.Forbidden)
        {
            return Stopped(Messages.ProblemForbidden);
        }

        if (problem.Code == SharedProblems.UpstreamUnavailable || problem.Code == SharedProblems.UpstreamTimeout)
        {
            return Failed(fileName, Unavailable);
        }

        return Failed(fileName, ServerError);
    }

    private static string InvalidReason(string? reason)
    {
        if (reason == "tooLarge")
        {
            return TooLarge;
        }

        if (reason == "unsupportedFormat")
        {
            return WrongType;
        }

        if (reason == "formatMismatch")
        {
            return TypeMismatch;
        }

        return ServerError;
    }

    private static UploadStep Stopped(string message)
    {
        return new UploadStep { Outcome = UploadOutcome.Failed, Message = message, Stops = true };
    }
}
