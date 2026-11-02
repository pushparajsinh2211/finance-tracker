using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FamilyLedger.Api.Infrastructure;
using FamilyLedger.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyLedger.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/privacy")]
public sealed class PrivacyController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PrivacyController> _logger;

    public PrivacyController(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<PrivacyController> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportMyData()
    {
        var response = await GetUserClient().Rpc(
            "export_my_data",
            new Dictionary<string, object>());
        var content = response.Content?.Trim();

        if (string.IsNullOrWhiteSpace(content) || content == "null")
        {
            return Problem("The data export could not be generated.");
        }

        var fileName = $"financehub-export-{DateTime.UtcNow:yyyy-MM-dd}.json";
        return File(Encoding.UTF8.GetBytes(content), "application/json", fileName);
    }

    [HttpGet("deletion-status")]
    public async Task<IActionResult> GetDeletionStatus()
    {
        var response = await GetUserClient().Rpc(
            "get_account_deletion_status",
            new Dictionary<string, object>());

        return Content(response.Content ?? "{}", "application/json");
    }

    [HttpDelete("account")]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request)
    {
        if (!AccountDeletionPolicy.HasValidConfirmation(request.Confirmation))
        {
            return BadRequest(new
            {
                message = $"Type {AccountDeletionPolicy.ConfirmationPhrase} exactly to confirm deletion."
            });
        }

        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(new { message = "User ID not found in token." });
        }

        var statusResponse = await GetUserClient().Rpc(
            "get_account_deletion_status",
            new Dictionary<string, object>());
        using var status = JsonDocument.Parse(statusResponse.Content ?? "{}");

        if (!status.RootElement.TryGetProperty("canDelete", out var canDelete) ||
            canDelete.ValueKind != JsonValueKind.True)
        {
            var blocker = status.RootElement.TryGetProperty("blocker", out var blockerElement)
                ? blockerElement.GetString()
                : "Account deletion is currently blocked.";
            return Conflict(new { message = blocker });
        }

        var supabaseUrl = RequiredConfiguration("Supabase:Url").TrimEnd('/');
        var serviceRoleKey = RequiredConfiguration("Supabase:ServiceRoleKey");
        var receiptPaths = await ListReceiptPaths(supabaseUrl, serviceRoleKey, userId);

        using var client = CreateServiceRoleClient(serviceRoleKey);
        using var deleteUser = await client.DeleteAsync(
            $"{supabaseUrl}/auth/v1/admin/users/{userId}?should_soft_delete=false");

        if (!deleteUser.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Supabase account deletion failed with status {StatusCode} for correlation ID {CorrelationId}",
                (int)deleteUser.StatusCode,
                HttpContext.TraceIdentifier);
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Account deletion failed",
                detail: "The identity provider did not complete the deletion. No account-deletion success was reported.");
        }

        if (receiptPaths.Count > 0)
        {
            using var removeRequest = new HttpRequestMessage(
                HttpMethod.Delete,
                $"{supabaseUrl}/storage/v1/object/receipts")
            {
                Content = JsonContent.Create(new { prefixes = receiptPaths })
            };
            AddServiceHeaders(removeRequest, serviceRoleKey);
            using var removeResponse = await client.SendAsync(removeRequest);

            if (!removeResponse.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Receipt cleanup failed after account deletion with status {StatusCode} for correlation ID {CorrelationId}",
                    (int)removeResponse.StatusCode,
                    HttpContext.TraceIdentifier);
            }
        }

        _logger.LogInformation(
            "Account deletion completed for correlation ID {CorrelationId}; receipt objects scheduled: {ReceiptCount}",
            HttpContext.TraceIdentifier,
            receiptPaths.Count);

        return Ok(new { message = "Your account and personal data have been deleted." });
    }

    private Postgrest.Client GetUserClient() =>
        AuthenticatedPostgrestClientFactory.Create(
            _configuration,
            Request.Headers.Authorization.FirstOrDefault());

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }

    private string RequiredConfiguration(string key) =>
        _configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} must be configured on the API server.");

    private HttpClient CreateServiceRoleClient(string serviceRoleKey)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        client.DefaultRequestHeaders.Add("apikey", serviceRoleKey);
        return client;
    }

    private async Task<List<string>> ListReceiptPaths(
        string supabaseUrl,
        string serviceRoleKey,
        Guid userId)
    {
        using var client = CreateServiceRoleClient(serviceRoleKey);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{supabaseUrl}/storage/v1/object/list/receipts")
        {
            Content = JsonContent.Create(new
            {
                prefix = userId.ToString(),
                limit = 1000,
                offset = 0,
                sortBy = new { column = "name", order = "asc" }
            })
        };
        AddServiceHeaders(request, serviceRoleKey);

        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Receipt listing failed with status {StatusCode} for correlation ID {CorrelationId}",
                (int)response.StatusCode,
                HttpContext.TraceIdentifier);
            throw new HttpRequestException("Receipt cleanup preparation failed.");
        }

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement
            .EnumerateArray()
            .Where(item => item.TryGetProperty("name", out _))
            .Select(item => $"{userId}/{item.GetProperty("name").GetString()}")
            .Where(path => !path.EndsWith("/", StringComparison.Ordinal))
            .ToList();
    }

    private static void AddServiceHeaders(HttpRequestMessage request, string serviceRoleKey)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        request.Headers.Add("apikey", serviceRoleKey);
    }
}
