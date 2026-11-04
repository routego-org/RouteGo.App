using System.Globalization;

namespace RouteGo.App.Features.Operator.Schedule;

/// <summary>The date to put trips on sale until, kept as typed so a refused value is shown again.</summary>
public sealed class ScheduleInput   // mutable class: must round-trip on re-render
{
    /// <summary>yyyy-MM-dd, as a date box sends it.</summary>
    public string? EndDate { get; set; }

    /// <summary>The date typed, or null when the box is empty or holds no date.</summary>
    public DateOnly? Date()
    {
        return ParseDate(EndDate);
    }

    /// <summary>A date as a date box sends it (yyyy-MM-dd), or null for anything else.</summary>
    public static DateOnly? ParseDate(string? text)
    {
        bool isDate = DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date);
        if (!isDate)
        {
            return null;
        }

        return date;
    }
}

/// <summary>A date must be given, from today to the latest date trips may be put on sale for.</summary>
public sealed class ScheduleValidator : AbstractValidator<ScheduleInput>
{
    public const string DateLabel = "Date";

    private readonly DateOnly _today;
    private readonly DateOnly _latest;

    public ScheduleValidator(DateOnly today, DateOnly latest)
    {
        _today = today;
        _latest = latest;
        RuleFor(x => x.EndDate)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(Messages.ValidationRequired(DateLabel))
            .Must(IsInRange).WithMessage(OutOfRangeMessage(latest));
    }

    /// <summary>"Choose a date from today to {latest date}."</summary>
    public static string OutOfRangeMessage(DateOnly latest)
    {
        return $"Choose a date from today to {IndiaTime.FormatDay(latest)}.";
    }

    private bool IsInRange(string? endDate)
    {
        DateOnly? date = ScheduleInput.ParseDate(endDate);
        if (date is null)
        {
            return false;
        }

        return date.Value >= _today && date.Value <= _latest;
    }
}
