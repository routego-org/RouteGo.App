namespace RouteGo.App.Infrastructure.Pipeline;

/// <summary>The result of one renewal attempt; Response is Identity's answer when the renewal failed.</summary>
public sealed record RenewalResult(RenewalOutcome Outcome, TokenPair? Tokens, HttpResponseMessage? Response);
