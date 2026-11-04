namespace RouteGo.App.Infrastructure;

/// <summary>
/// Reads a setting that names a web origin the security policy allows (for example where bus
/// photos are shown from). Such a setting may be left out — the policy then simply does not allow
/// that origin — but a value that is set must be a plain http or https origin, or the app refuses
/// to start, naming the key.
/// </summary>
public static class OptionalOrigin
{
    /// <summary>The origin ("https://host[:port]") the key names, or null when the key is not set.</summary>
    public static string? ReadOrigin(this IConfiguration configuration, string key)
    {
        string? value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        bool isAbsolute = Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? address);
        bool isWeb = isAbsolute && (address!.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp);
        if (!isWeb)
        {
            throw new InvalidOperationException(
                $"Configuration {key} must be an http or https origin such as https://photos.example.com, but was '{value}'.");
        }

        return address!.GetLeftPart(UriPartial.Authority);
    }
}
