namespace PitCrew.Dashboard.Adapters.GitHub;

internal sealed record GitHubAppJwtPayload(long Iss, long Iat, long Exp);
