namespace RouteGo.App.Common.UI;

/// <summary>
/// The token pair Identity returns when a session starts or is renewed (sign-in, email
/// verification, refresh). The portal keeps it in the encrypted session cookie and never shows it.
/// </summary>
public sealed record TokenPair
{
    public required string AccessToken { get; init; }

    public required DateTimeOffset AccessTokenExpiresAt { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTimeOffset RefreshTokenExpiresAt { get; init; }

    public required string TokenType { get; init; }
}
