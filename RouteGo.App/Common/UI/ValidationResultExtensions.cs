using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace RouteGo.App.Common.UI;

/// <summary>
/// Maps a LOCAL FluentValidation result onto ModelState with the "Input." prefix, so error keys
/// match asp-validation-for="Input.X" exactly like the backend's field errors do.
/// </summary>
public static class ValidationResultExtensions
{
    public static void AddToModelState(this ValidationResult result,
        ModelStateDictionary modelState, string? prefix = null)
    {
        foreach (ValidationFailure error in result.Errors)
        {
            string key = string.IsNullOrEmpty(prefix)
                ? error.PropertyName
                : $"{prefix}.{error.PropertyName}";
            modelState.AddModelError(key, error.ErrorMessage);
        }
    }
}
