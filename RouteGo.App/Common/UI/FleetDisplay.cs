using System.Globalization;

namespace RouteGo.App.Common.UI;

/// <summary>
/// The rules every Fleet page shares, written once: how a route is named, how a time from start is
/// shown, the words for Fleet's statuses, and what a page shows for the refusals every operator
/// page can meet (an account still being set up, a record that is not there).
/// </summary>
public static class FleetDisplay
{
    public const string OperatorProfileNotReady = "OPERATOR_PROFILE_NOT_READY";
    public const string NotFound = "NOT_FOUND";

    /// <summary>How many items one page of a Fleet list shows.</summary>
    public const int ListPageSize = 20;

    private const int MinutesPerHour = 60;

    /// <summary>"Showing 21–40 of 45 routes" for one loaded page of a list.</summary>
    /// <param name="page">The 1-based page shown.</param>
    /// <param name="shownCount">How many items the page holds.</param>
    /// <param name="totalCount">How many items match across all pages.</param>
    /// <param name="items">The items' plural name, in lower case ("routes").</param>
    public static string ShowingText(int page, int shownCount, int totalCount, string items)
    {
        int from = ((page - 1) * ListPageSize) + 1;
        int to = from + shownCount - 1;
        return $"Showing {from}–{to} of {totalCount} {items}";
    }

    /// <summary>How many pages the matching items fill.</summary>
    public static int PageCount(int totalCount)
    {
        return (totalCount + ListPageSize - 1) / ListPageSize;
    }

    /// <summary>
    /// "{first city} → {last city}", with "via {stop}, {stop}" when the route stops in between —
    /// Fleet gives routes no name of their own. The city names are in stop order.
    /// </summary>
    public static string RouteName(IReadOnlyList<string> cityNames)
    {
        string name = cityNames[0] + " → " + cityNames[cityNames.Count - 1];
        if (cityNames.Count <= 2)
        {
            return name;
        }

        var between = new List<string>();
        for (int index = 1; index < cityNames.Count - 1; index++)
        {
            between.Add(cityNames[index]);
        }

        return name + " via " + string.Join(", ", between);
    }

    /// <summary>A duration in whole minutes, shown short: "3 h 30 min", "3 h", "45 min", "0 min".</summary>
    public static string Duration(int minutes)
    {
        int hours = minutes / MinutesPerHour;
        int rest = minutes % MinutesPerHour;
        if (hours == 0)
        {
            return rest.ToString(CultureInfo.InvariantCulture) + " min";
        }

        string text = hours.ToString(CultureInfo.InvariantCulture) + " h";
        if (rest == 0)
        {
            return text;
        }

        return text + " " + rest.ToString(CultureInfo.InvariantCulture) + " min";
    }

    /// <summary>
    /// A photo address from Fleet when it is a plain web address (http or https); null for anything
    /// else, so a bad value can never become a script address on the page.
    /// </summary>
    public static string? SafePhotoAddress(string? photoUrl)
    {
        bool isAbsolute = Uri.TryCreate(photoUrl, UriKind.Absolute, out Uri? address);
        if (!isAbsolute)
        {
            return null;
        }

        if (address!.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp)
        {
            return null;
        }

        return address.AbsoluteUri;
    }

    /// <summary>The catalog's word for a bus, route or schedule status as Fleet spells it.</summary>
    public static string StatusLabel(string status)
    {
        if (status == "active")
        {
            return Messages.StateActive;
        }

        return Messages.StateInactive;
    }

    /// <summary>
    /// Whether the refusal means the page must show no record at all: the record is not there, is
    /// another company's, or the operator's account is not ready yet.
    /// </summary>
    public static bool HidesRecord(ApiProblem problem)
    {
        return problem.Code == OperatorProfileNotReady
            || problem.Code == NotFound
            || problem.Code == SharedProblems.Forbidden;
    }

    /// <summary>
    /// The page-level alert for a Fleet refusal. <paramref name="entity"/> names the record in the
    /// not-found message ("route"); every other code falls back to the portal-wide words.
    /// </summary>
    public static PageAlert ToAlert(ApiProblem problem, string entity, string correlationId)
    {
        if (problem.Code == OperatorProfileNotReady)
        {
            return PageAlert.Warning(Messages.ProblemOperatorProfileNotReady);
        }

        if (problem.Code == NotFound)
        {
            return PageAlert.Danger(Messages.ProblemNotFound(entity));
        }

        return SharedProblems.ToAlert(problem, correlationId);
    }
}
