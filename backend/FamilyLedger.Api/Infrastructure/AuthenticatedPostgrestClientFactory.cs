namespace FamilyLedger.Api.Infrastructure;

public static class AuthenticatedPostgrestClientFactory
{
    public static Postgrest.Client Create(IConfiguration configuration, string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Missing or invalid Authorization header.");
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        var url = configuration["Supabase:Url"];
        var key = configuration["Supabase:AnonKey"];

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Supabase URL and anonymous key must be configured.");
        }

        var client = new Postgrest.Client($"{url.TrimEnd('/')}/rest/v1");
        client.GetHeaders = () => new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {token}",
            ["apikey"] = key
        };

        return client;
    }
}
