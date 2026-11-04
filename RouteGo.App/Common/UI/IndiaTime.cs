using System.Globalization;

namespace RouteGo.App.Common.UI;

/// <summary>
/// Shows service times (always UTC) in India Standard Time, in the design system's format:
/// "2 Oct 2026, 4:00 PM IST". IST is a fixed UTC+05:30 with no daylight saving, so a fixed offset
/// is exact and needs no time-zone database in the container.
/// </summary>
public static class IndiaTime
{
    private static readonly TimeSpan _offset = new TimeSpan(5, 30, 0);

    /// <summary>A date and time, for example "2 Oct 2026, 10:45 AM IST".</summary>
    public static string Format(DateTimeOffset moment)
    {
        DateTimeOffset local = moment.ToOffset(_offset);
        string text = local.ToString("d MMM yyyy, h:mm tt", CultureInfo.InvariantCulture);
        return text + " IST";
    }

    /// <summary>The IST date of a moment, without the time, for example "2 Oct 2026".</summary>
    public static string FormatDate(DateTimeOffset moment)
    {
        DateTimeOffset local = moment.ToOffset(_offset);
        return local.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>The time of day of a moment in IST, for example "10:30 PM IST".</summary>
    public static string FormatTime(DateTimeOffset moment)
    {
        DateTimeOffset local = moment.ToOffset(_offset);
        return local.ToString("h:mm tt", CultureInfo.InvariantCulture) + " IST";
    }

    /// <summary>The IST day and month of a moment, for example "26 Oct" — for a date the reader already knows the year of.</summary>
    public static string FormatDayMonth(DateTimeOffset moment)
    {
        DateTimeOffset local = moment.ToOffset(_offset);
        return local.ToString("d MMM", CultureInfo.InvariantCulture);
    }

    /// <summary>A calendar date (a travel date, a last date on sale), for example "22 Oct 2026".</summary>
    public static string FormatDay(DateOnly date)
    {
        return date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>Today's date in India at the moment given.</summary>
    public static DateOnly Today(DateTimeOffset now)
    {
        return DateOnly.FromDateTime(now.ToOffset(_offset).DateTime);
    }

    /// <summary>
    /// Formats a problem extension holding an ISO date-time; null when the value is missing or
    /// not a date-time.
    /// </summary>
    public static string? FormatIso(string? isoMoment)
    {
        if (!DateTimeOffset.TryParse(isoMoment, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset moment))
        {
            return null;
        }

        return Format(moment);
    }
}
