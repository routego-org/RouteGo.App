namespace RouteGo.App.Features.Operator.Trips;

/// <summary>One trip's new fares as typed, one box per fare of the trip in its order.</summary>
public sealed class TripFaresInput   // mutable class: must round-trip on re-render
{
    public Guid TripId { get; set; }

    public List<ScheduleFormFare> Fares { get; set; } = new List<ScheduleFormFare>();

    /// <summary>The fares as Fleet takes them; only called once every box holds an allowed fare.</summary>
    public List<ClassFare> ClassFares()
    {
        var fares = new List<ClassFare>();
        foreach (ScheduleFormFare fare in Fares)
        {
            fares.Add(new ClassFare(fare.SeatClass!, TripDisplay.Fare(fare.Fare)!.Value));
        }

        return fares;
    }
}

/// <summary>The trip to cancel and why, as typed.</summary>
public sealed class TripCancelInput   // mutable class: must round-trip on re-render
{
    public Guid TripId { get; set; }

    public string? Reason { get; set; }
}

/// <summary>Each fare must be whole rupees from ₹50 to ₹10,000.</summary>
public sealed class TripFaresValidator : AbstractValidator<TripFaresInput>
{
    public TripFaresValidator()
    {
        RuleFor(x => x.Fares).Custom(CheckFares);
    }

    /// <summary>The key of a fare box in the page's errors ("Fares[1].Fare").</summary>
    public static string FareField(int index)
    {
        return $"{nameof(TripFaresInput.Fares)}[{index}].{nameof(ScheduleFormFare.Fare)}";
    }

    private static void CheckFares(List<ScheduleFormFare> fares, ValidationContext<TripFaresInput> context)
    {
        for (int index = 0; index < fares.Count; index++)
        {
            if (TripDisplay.Fare(fares[index].Fare) is null)
            {
                context.AddFailure(FareField(index), Messages.ValidationFare);
            }
        }
    }
}

/// <summary>A cancellation needs a reason of 10 to 500 characters, as Fleet keeps it.</summary>
public sealed class TripCancelValidator : AbstractValidator<TripCancelInput>
{
    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 500;

    public TripCancelValidator()
    {
        RuleFor(x => x.Reason).Must(IsReasonLength).WithMessage(Messages.ValidationReasonLength);
    }

    private static bool IsReasonLength(string? reason)
    {
        int length = (reason ?? string.Empty).Trim().Length;
        return length >= MinReasonLength && length <= MaxReasonLength;
    }
}
