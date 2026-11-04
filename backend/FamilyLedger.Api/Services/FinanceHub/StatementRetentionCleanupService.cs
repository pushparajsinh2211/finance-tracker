using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FamilyLedger.Api.Services.FinanceHub;

public sealed class StatementRetentionCleanupService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<StatementRetentionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const string Bucket = "financehub-statements";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        await CleanupAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
            await CleanupAsync(stoppingToken);
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var supabaseUrl = configuration["Supabase:Url"];
        var serviceRoleKey = configuration["Supabase:ServiceRoleKey"];
        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            logger.LogInformation("Statement retention cleanup is disabled because service-role configuration is absent.");
            return;
        }

        try
        {
            var threshold = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
            using var lookup = CreateRequest(
                HttpMethod.Get,
                $"{supabaseUrl.TrimEnd('/')}/rest/v1/statement_import_files" +
                $"?deleted_at=is.null&delete_after=lte.{threshold}&select=id,object_path&limit=100",
                serviceRoleKey);
            var client = httpClientFactory.CreateClient();
            using var response = await client.SendAsync(lookup, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var id = item.GetProperty("id").GetGuid();
                var objectPath = item.GetProperty("object_path").GetString();
                if (string.IsNullOrWhiteSpace(objectPath))
                    continue;

                using var delete = CreateRequest(
                    HttpMethod.Delete,
                    $"{supabaseUrl.TrimEnd('/')}/storage/v1/object/{Bucket}/{EscapePath(objectPath)}",
                    serviceRoleKey);
                using var deleteResponse = await client.SendAsync(delete, cancellationToken);
                if (!deleteResponse.IsSuccessStatusCode &&
                    deleteResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    logger.LogWarning(
                        "Expired statement object deletion failed with status {StatusCode}.",
                        (int)deleteResponse.StatusCode);
                    continue;
                }

                using var mark = CreateRequest(
                    HttpMethod.Patch,
                    $"{supabaseUrl.TrimEnd('/')}/rest/v1/statement_import_files?id=eq.{id:D}",
                    serviceRoleKey);
                mark.Headers.Add("Prefer", "return=minimal");
                mark.Content = JsonContent.Create(new { deleted_at = DateTimeOffset.UtcNow });
                using var markResponse = await client.SendAsync(mark, cancellationToken);
                if (!markResponse.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Expired statement metadata update failed with status {StatusCode}.",
                        (int)markResponse.StatusCode);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Statement retention cleanup failed.");
        }
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string serviceRoleKey)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        request.Headers.Add("apikey", serviceRoleKey);
        return request;
    }

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
