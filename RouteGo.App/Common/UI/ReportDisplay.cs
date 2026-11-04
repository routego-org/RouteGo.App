using System.Globalization;

namespace RouteGo.App.Common.UI;

/// <summary>
/// The rules every reports page shares, written once: whole-rupee money with a minus sign, the day
/// range a person may ask for, and the note that explains how revenue and refunds are counted. The
/// words come from the message catalog's sales labels and the reports page rules.
/// </summary>
public static class ReportDisplay
{
    /// <summary>The most days one range may cover, both ends included; the portal's own limit.</summary>
    public const int MaxRangeDays = 31;

    /// <summary>A page opens on this many days ending today.</summary>
    public const int DefaultRangeDays = 7;

    public const string RevenueNote = "Revenue counts a booking on the day it was confirmed, even if it was cancelled later. Refunds count on the day the money was paid back.";

    private const string DateFormat = "yyyy-MM-dd";
    private const string MinusSign = "−";

    /// <summary>"₹10,800" in whole rupees; a negative amount keeps its sign as "−₹1,000".</summary>
    public static string Rupees(decimal amount)
    {
        decimal whole = Math.Round(amount, MidpointRounding.AwayFromZero);
        string text = "₹" + Math.Abs(whole).ToString("N0", CultureInfo.InvariantCulture);
        if (whole < 0)
        {
            return MinusSign + text;
        }

        return text;
    }

    /// <summary>A day as the address and Reporting write it: yyyy-MM-dd.</summary>
    public static string WireDate(DateOnly date)
    {
        return date.ToString(DateFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>The day a box holds; null when it is empty or not a date written yyyy-MM-dd.</summary>
    public static DateOnly? ParseDay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        bool isDate = DateOnly.TryParseExact(text.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date);
        if (!isDate)
        {
            return null;
        }

        return date;
    }

    /// <summary>
    /// Checks a typed From and To before anything is sent: each must be a date, To must not be
    /// before From, and the range may cover at most 31 days.
    /// </summary>
    public static ReportRangeCheck CheckRange(string? from, string? to)
    {
        DateOnly? first = ParseDay(from);
        DateOnly? last = ParseDay(to);
        if (first is null || last is null)
        {
            return new ReportRangeCheck
            {
                FromError = ErrorWhenNotDate(first),
                ToError = ErrorWhenNotDate(last),
            };
        }

        int days = last.Value.DayNumber - first.Value.DayNumber + 1;
        if (days < 1 || days > MaxRangeDays)
        {
            return new ReportRangeCheck { RangeError = Messages.ValidationDateRange };
        }

        return new ReportRangeCheck { Range = new ReportRange(first.Value, last.Value) };
    }

    private static string? ErrorWhenNotDate(DateOnly? day)
    {
        if (day is null)
        {
            return Messages.ValidationFormat("date");
        }

        return null;
    }
}
