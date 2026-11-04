namespace RouteGo.App.Infrastructure.Pipeline;

/// <summary>
/// Thrown when the person's session can no longer be used: Identity refused to renew it, or a
/// call made with the person's token answered UNAUTHORIZED. SessionEndedMiddleware turns it into
/// "remove the session and send the person to sign in".
/// </summary>
public sealed class SessionEndedException : Exception
{
    public SessionEndedException(string message)
        : base(message)
    {
    }
}
