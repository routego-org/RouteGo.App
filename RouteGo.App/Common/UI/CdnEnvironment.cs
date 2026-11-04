namespace RouteGo.App.Common.UI;

/// <summary>
/// The single place the CDN variable name lives. CDN_BASE_URL is set only as an environment
/// variable on the Lambda function, per release — never appsettings — and is unset in dev and
/// tests, which flips CdnTagHelper to local paths and the CSP to 'self' only
/// (references/static-assets.md).
/// </summary>
public static class CdnEnvironment
{
    public const string VariableName = "CDN_BASE_URL";

    // Unset in dev/tests -> local asset paths and a 'self'-only CSP.
    public static string? BaseUrl
    {
        get { return Environment.GetEnvironmentVariable(VariableName); }
    }
}
