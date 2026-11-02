using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FamilyLedger.Api.Health;

public sealed class SupabaseHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    public SupabaseHealthCheck(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var supabaseUrl = _configuration["Supabase:Url"];
        var supabaseKey = _configuration["Supabase:AnonKey"];

        if (!Uri.TryCreate(supabaseUrl, UriKind.Absolute, out var baseUri) ||
            string.IsNullOrWhiteSpace(supabaseKey))
        {
            return HealthCheckResult.Unhealthy("Supabase configuration is incomplete.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(baseUri, "/auth/v1/health"));
            request.Headers.Add("apikey", supabaseKey);

            var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Supabase Auth returned an unhealthy response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Supabase Auth health check timed out.");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("Supabase Auth is unavailable.");
        }
    }
}
