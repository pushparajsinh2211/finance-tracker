using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FamilyLedger.Api.Services.FinanceHub;

public sealed record StatementRetentionResult(
    bool Retained,
    DateTimeOffset? DeleteAfter,
    int? RetentionDays,
    string? Message = null);

public interface IStatementRetentionService
{
    Task<StatementRetentionResult> RetainAsync(
        IFormFile file,
        Guid userId,
        Guid importRunId,
        string checksum,
        int retentionDays,
        string authorization,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid importRunId,
        string authorization,
        CancellationToken cancellationToken);
}

public sealed class SupabaseStatementRetentionService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<SupabaseStatementRetentionService> logger) : IStatementRetentionService
{
    private const string Bucket = "financehub-statements";

    public async Task<StatementRetentionResult> RetainAsync(
        IFormFile file,
        Guid userId,
        Guid importRunId,
        string checksum,
        int retentionDays,
        string authorization,
        CancellationToken cancellationToken)
    {
        if (retentionDays is not (30 or 90))
            throw new InvalidOperationException("Statement retention must be 30 or 90 days.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var mimeType = extension switch
        {
            ".csv" => "text/csv",
            ".pdf" => "application/pdf",
            _ => throw new InvalidOperationException("Only CSV and PDF statements can be retained.")
        };
        var objectPath = $"{userId:D}/{importRunId:D}/statement{extension}";

        await using var input = file.OpenReadStream();
        using var bytes = new MemoryStream();
        await input.CopyToAsync(bytes, cancellationToken);

        using var upload = CreateRequest(
            HttpMethod.Post,
            $"/storage/v1/object/{Bucket}/{EscapePath(objectPath)}",
            authorization);
        upload.Headers.Add("x-upsert", "true");
        upload.Content = new ByteArrayContent(bytes.ToArray());
        upload.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

        var client = httpClientFactory.CreateClient();
        using var uploadResponse = await client.SendAsync(upload, cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Retained statement upload failed with status {StatusCode}.",
                (int)uploadResponse.StatusCode);
            throw new InvalidOperationException("The import succeeded, but the original statement could not be retained.");
        }

        var metadataPayload = new
        {
            p_import_run_id = importRunId,
            p_object_path = objectPath,
            p_original_file_name = SafeFileName(file.FileName),
            p_mime_type = mimeType,
            p_byte_size = file.Length,
            p_checksum = checksum,
            p_retention_days = retentionDays
        };

        using var metadata = CreateRequest(
            HttpMethod.Post,
            "/rest/v1/rpc/record_retained_statement",
            authorization);
        metadata.Content = JsonContent.Create(metadataPayload);
        using var metadataResponse = await client.SendAsync(metadata, cancellationToken);
        var metadataJson = await metadataResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!metadataResponse.IsSuccessStatusCode)
        {
            await DeleteObjectAsync(client, objectPath, authorization, cancellationToken);
            logger.LogWarning(
                "Retained statement metadata failed with status {StatusCode}.",
                (int)metadataResponse.StatusCode);
            throw new InvalidOperationException("The import succeeded, but retention metadata could not be saved.");
        }

        using var document = JsonDocument.Parse(metadataJson);
        var root = document.RootElement;
        return new StatementRetentionResult(
            true,
            root.TryGetProperty("deleteAfter", out var deleteAfter)
                ? deleteAfter.GetDateTimeOffset()
                : null,
            retentionDays);
    }

    public async Task DeleteAsync(
        Guid importRunId,
        string authorization,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var lookup = CreateRequest(
            HttpMethod.Get,
            $"/rest/v1/statement_import_files?import_run_id=eq.{importRunId:D}&deleted_at=is.null&select=object_path&limit=1",
            authorization);
        using var lookupResponse = await client.SendAsync(lookup, cancellationToken);
        var body = await lookupResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!lookupResponse.IsSuccessStatusCode)
            throw new InvalidOperationException("Retained statement metadata could not be loaded.");

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.GetArrayLength() == 0)
            throw new KeyNotFoundException("No retained statement exists for this import.");

        var objectPath = document.RootElement[0].GetProperty("object_path").GetString()
            ?? throw new InvalidOperationException("Retained statement metadata is incomplete.");

        await DeleteObjectAsync(client, objectPath, authorization, cancellationToken);

        using var markDeleted = CreateRequest(
            HttpMethod.Post,
            "/rest/v1/rpc/mark_retained_statement_deleted",
            authorization);
        markDeleted.Content = JsonContent.Create(new { p_import_run_id = importRunId });
        using var markResponse = await client.SendAsync(markDeleted, cancellationToken);
        if (!markResponse.IsSuccessStatusCode)
            throw new InvalidOperationException("The retained statement was deleted, but its metadata could not be updated.");
    }

    private async Task DeleteObjectAsync(
        HttpClient client,
        string objectPath,
        string authorization,
        CancellationToken cancellationToken)
    {
        using var delete = CreateRequest(
            HttpMethod.Delete,
            $"/storage/v1/object/{Bucket}/{EscapePath(objectPath)}",
            authorization);
        using var response = await client.SendAsync(delete, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("The retained statement file could not be deleted.");
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string relativePath,
        string authorization)
    {
        var supabaseUrl = configuration["Supabase:Url"];
        var anonKey = configuration["Supabase:AnonKey"];
        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(anonKey))
            throw new InvalidOperationException("Supabase retention configuration is incomplete.");

        var request = new HttpRequestMessage(
            method,
            supabaseUrl.TrimEnd('/') + relativePath);
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        request.Headers.Add("apikey", anonKey);
        return request;
    }

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static string SafeFileName(string value)
    {
        var name = Path.GetFileName(value);
        var sanitized = new string(name
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or ' ')
            .ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(sanitized)
            ? "statement"
            : sanitized[..Math.Min(sanitized.Length, 180)];
    }
}
