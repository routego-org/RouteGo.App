namespace RouteGo.App.Features.Admin.SeatAllocations;

/// <summary>The hold cancellation form: which booking's hold, and why.</summary>
public sealed class HoldCancelInput
{
    public Guid BookingId { get; set; }

    public string? Reason { get; set; }
}

/// <summary>A hold cancellation needs a reason of 10 to 500 characters, kept in Seat Inventory's audit record.</summary>
public sealed class HoldCancelValidator : AbstractValidator<HoldCancelInput>
{
    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 500;

    public HoldCancelValidator()
    {
        RuleFor(x => x.Reason).Must(IsReasonLength).WithMessage(Messages.ValidationReasonLength);
    }

    private static bool IsReasonLength(string? reason)
    {
        int length = (reason ?? string.Empty).Trim().Length;
        return length >= MinReasonLength && length <= MaxReasonLength;
    }
}
