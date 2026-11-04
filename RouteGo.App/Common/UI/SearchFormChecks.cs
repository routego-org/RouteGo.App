using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace RouteGo.App.Common.UI;

/// <summary>
/// The search form's checks before Search is asked — Search's own rules, checked first so the
/// person sees the problem at once: both cities picked from the suggestions and different, a travel
/// date from today, and prices as whole rupees with Price from not above Price up to.
/// </summary>
public static class SearchFormChecks
{
    private const char PickSeparator = '|';

    // Search's field names in a VALIDATION_FAILED answer for the fields the form shows; any other
    // field (a choice list someone changed by hand) is told at the top with the problem's title.
    private static readonly Dictionary<string, string> _formFieldsByServiceField =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fromCityId"] = nameof(SearchFormInput.FromCity),
            ["toCityId"] = nameof(SearchFormInput.ToCity),
            ["date"] = nameof(SearchFormInput.Date),
            ["minFare"] = nameof(SearchFormInput.MinFare),
            ["maxFare"] = nameof(SearchFormInput.MaxFare),
        };

    /// <summary>
    /// Takes a city chosen from the list drawn without the page script ("{id}|{name}") as that
    /// box's pick, filling its text, identifier and name.
    /// </summary>
    public static void ApplyPicks(SearchFormInput input)
    {
        SuggestedCity? from = ReadPick(input.FromPick);
        if (from != null)
        {
            input.FromCity = from.Name;
            input.FromCityId = from.CityId.ToString();
            input.FromCityName = from.Name;
        }

        SuggestedCity? to = ReadPick(input.ToPick);
        if (to != null)
        {
            input.ToCity = to.Name;
            input.ToCityId = to.CityId.ToString();
            input.ToCityName = to.Name;
        }
    }

    /// <summary>The value a no-script choice posts for a city.</summary>
    public static string PickValue(SuggestedCity city)
    {
        return city.CityId.ToString() + PickSeparator + city.Name;
    }

    /// <summary>The checks for a posted form, where a city counts only when picked from the suggestions.</summary>
    public static Dictionary<string, string> CheckForm(SearchFormInput input, DateOnly today)
    {
        var errors = new Dictionary<string, string>();
        CheckCity(errors, nameof(SearchFormInput.FromCity), SearchForm.FromLabel, input.FromCity, IsPicked(input.FromCity, input.FromCityId, input.FromCityName));
        CheckCity(errors, nameof(SearchFormInput.ToCity), SearchForm.ToLabel, input.ToCity, IsPicked(input.ToCity, input.ToCityId, input.ToCityName));
        CheckDifferentCities(errors, input);
        CheckDate(errors, input.Date, today);
        CheckPrices(errors, input);
        return errors;
    }

    /// <summary>The checks for a results address, which names its cities by identifier only.</summary>
    public static Dictionary<string, string> CheckAddress(SearchFormInput input, DateOnly today)
    {
        var errors = new Dictionary<string, string>();
        if (!Guid.TryParse(input.FromCityId, out _))
        {
            errors[nameof(SearchFormInput.FromCity)] = SearchForm.ChooseCity;
        }

        if (!Guid.TryParse(input.ToCityId, out _))
        {
            errors[nameof(SearchFormInput.ToCity)] = SearchForm.ChooseCity;
        }

        CheckDifferentCities(errors, input);
        CheckDate(errors, input.Date, today);
        CheckPrices(errors, input);
        return errors;
    }

    /// <summary>A yyyy-MM-dd date, or null when the text is not one.</summary>
    public static DateOnly? ReadDate(string? text)
    {
        bool isDate = DateOnly.TryParseExact(text, SearchForm.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date);
        if (!isDate)
        {
            return null;
        }

        return date;
    }

    /// <summary>Puts each failed field's message on the form's field ("Search.FromCity").</summary>
    public static void AddErrors(ModelStateDictionary modelState, IReadOnlyDictionary<string, string> errors)
    {
        foreach (KeyValuePair<string, string> error in errors)
        {
            modelState.AddModelError(SearchForm.Prefix + "." + error.Key, error.Value);
        }
    }

    /// <summary>The form's message for each field Search refused that the form shows.</summary>
    public static Dictionary<string, string> FieldErrors(IReadOnlyDictionary<string, string[]> serviceErrors)
    {
        var errors = new Dictionary<string, string>();
        foreach (string serviceField in serviceErrors.Keys)
        {
            if (!_formFieldsByServiceField.TryGetValue(serviceField, out string? formField))
            {
                continue;
            }

            errors[formField] = FieldMessage(formField);
        }

        return errors;
    }

    /// <summary>Whether Search refused a field the form does not show as a box.</summary>
    public static bool NamesOtherFields(IReadOnlyDictionary<string, string[]> serviceErrors)
    {
        return serviceErrors.Keys.Any(serviceField => !_formFieldsByServiceField.ContainsKey(serviceField));
    }

    private static string FieldMessage(string formField)
    {
        switch (formField)
        {
            case nameof(SearchFormInput.Date):
                return SearchForm.TodayOrLater;
            case nameof(SearchFormInput.MinFare):
                return Messages.ValidationFormat(SearchForm.PriceFromLabel.ToLowerInvariant());
            case nameof(SearchFormInput.MaxFare):
                return Messages.ValidationFormat(SearchForm.PriceUpToLabel.ToLowerInvariant());
            default:
                return SearchForm.ChooseCity;
        }
    }

    // A pick counts only while the box still shows the picked name: text typed over it afterwards
    // (without the page script nothing empties the hidden identifier) is typed text again.
    private static bool IsPicked(string? text, string? cityId, string? cityName)
    {
        if (!Guid.TryParse(cityId, out _) || string.IsNullOrWhiteSpace(cityName) || text is null)
        {
            return false;
        }

        return string.Equals(text.Trim(), cityName.Trim(), StringComparison.Ordinal);
    }

    private static void CheckCity(Dictionary<string, string> errors, string field, string label, string? text, bool isPicked)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            errors[field] = Messages.ValidationRequired(label);
            return;
        }

        if (!isPicked)
        {
            errors[field] = SearchForm.ChooseCity;
        }
    }

    private static void CheckDifferentCities(Dictionary<string, string> errors, SearchFormInput input)
    {
        bool isFromFailed = errors.ContainsKey(nameof(SearchFormInput.FromCity));
        bool isToFailed = errors.ContainsKey(nameof(SearchFormInput.ToCity));
        if (isFromFailed || isToFailed)
        {
            return;
        }

        if (Guid.Parse(input.FromCityId!) == Guid.Parse(input.ToCityId!))
        {
            errors[nameof(SearchFormInput.ToCity)] = SearchForm.DifferentCities;
        }
    }

    private static void CheckDate(Dictionary<string, string> errors, string? text, DateOnly today)
    {
        string field = nameof(SearchFormInput.Date);
        if (string.IsNullOrWhiteSpace(text))
        {
            errors[field] = Messages.ValidationRequired(SearchForm.DateLabel);
            return;
        }

        DateOnly? date = ReadDate(text.Trim());
        if (date is null)
        {
            errors[field] = Messages.ValidationFormat(SearchForm.DateLabel.ToLowerInvariant());
            return;
        }

        if (date.Value < today)
        {
            errors[field] = SearchForm.TodayOrLater;
        }
    }

    private static void CheckPrices(Dictionary<string, string> errors, SearchFormInput input)
    {
        bool isMinRead = TryReadFare(input.MinFare, out int? minFare);
        bool isMaxRead = TryReadFare(input.MaxFare, out int? maxFare);
        if (!isMinRead)
        {
            errors[nameof(SearchFormInput.MinFare)] = FieldMessage(nameof(SearchFormInput.MinFare));
        }

        if (!isMaxRead)
        {
            errors[nameof(SearchFormInput.MaxFare)] = FieldMessage(nameof(SearchFormInput.MaxFare));
        }

        if (minFare != null && maxFare != null && minFare.Value > maxFare.Value)
        {
            errors[nameof(SearchFormInput.MinFare)] = SearchForm.PriceOrder;
        }
    }

    // Whole rupees from 0, digits only; an empty box is no price and reads fine.
    private static bool TryReadFare(string? text, out int? fare)
    {
        fare = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            return false;
        }

        fare = value;
        return true;
    }

    private static SuggestedCity? ReadPick(string? pick)
    {
        if (string.IsNullOrWhiteSpace(pick))
        {
            return null;
        }

        int separator = pick.IndexOf(PickSeparator, StringComparison.Ordinal);
        if (separator < 0 || !Guid.TryParse(pick[..separator], out Guid cityId))
        {
            return null;
        }

        string name = pick[(separator + 1)..].Trim();
        if (name.Length == 0)
        {
            return null;
        }

        return new SuggestedCity(cityId, name);
    }
}
