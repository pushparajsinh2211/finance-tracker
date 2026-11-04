using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FamilyLedger.Api.Services.FinanceHub;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyLedger.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/financehub/imports")]
public sealed class FinanceHubImportsController : ControllerBase
{
    private readonly IStatementParser _parser;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IStatementRetentionService _retentionService;

    public FinanceHubImportsController(
        IStatementParser parser,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IStatementRetentionService retentionService)
    {
        _parser = parser;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _retentionService = retentionService;
    }

    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(StatementParser.MaximumFileBytes)]
    public async Task<IActionResult> Preview(
        [FromForm] StatementImportRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(request);
        if (validation != null)
            return BadRequest(new { Message = validation });

        try
        {
            var parsed = await _parser.ParseAsync(
                request.File,
                NormalizeSourceType(request.SourceType),
                request.Password,
                cancellationToken);

            return Ok(new StatementPreviewResponse(
                parsed.Parser,
                parsed.ParserVersion,
                parsed.FileType,
                parsed.FileChecksum,
                parsed.Transactions.Count,
                parsed.Transactions.Count(transaction => transaction.NeedsReview),
                parsed.Errors,
                parsed.Transactions.Take(100).ToArray(),
                parsed.Reconciliation,
                parsed.UsedOcr));
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            return UnprocessableEntity(new { Message = ex.Message });
        }
        catch (Exception)
        {
            return UnprocessableEntity(new
            {
                Message = "The statement could not be parsed. Verify the file format and password."
            });
        }
    }

    [HttpPost("commit")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(StatementParser.MaximumFileBytes)]
    public async Task<IActionResult> Commit(
        [FromForm] StatementImportRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(request);
        if (validation != null)
            return BadRequest(new { Message = validation });

        if (!TryGetUserId(out _))
            return Unauthorized(new { Message = "User ID not found in token." });

        try
        {
            var sourceType = NormalizeSourceType(request.SourceType);
            var parsed = await _parser.ParseAsync(
                request.File,
                sourceType,
                request.Password,
                cancellationToken);

            if (parsed.Transactions.Count == 0)
                return UnprocessableEntity(new { Message = "No transactions are available to import." });

            if (parsed.Reconciliation.Status == "mismatch" &&
                !request.ConfirmReconciliationMismatch)
            {
                return UnprocessableEntity(new
                {
                    Message = "The statement totals do not reconcile. Review the variance and explicitly confirm before importing.",
                    Reconciliation = parsed.Reconciliation
                });
            }

            var idempotencyKey = CreateIdempotencyKey(
                parsed.FileChecksum,
                sourceType,
                request.AccountName,
                request.MaskedIdentifier);

            var rpcPayload = new
            {
                p_source_type = sourceType,
                p_file_type = parsed.FileType,
                p_account_name = request.AccountName.Trim(),
                p_masked_identifier = NormalizeMaskedIdentifier(request.MaskedIdentifier),
                p_idempotency_key = idempotencyKey,
                p_transactions = parsed.Transactions.Select(transaction => new
                {
                    occurredAt = transaction.OccurredAt,
                    amount = transaction.Amount,
                    direction = transaction.Direction,
                    narration = transaction.Narration,
                    externalReference = transaction.ExternalReference,
                    paymentRail = transaction.PaymentRail,
                    transactionType = transaction.TransactionType,
                    needsReview = transaction.NeedsReview,
                    occurrenceIndex = transaction.OccurrenceIndex
                })
            };

            using var message = CreateRpcRequest(rpcPayload);
            var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(message, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new { Message = "The validated statement could not be committed." });
            }

            var result = JsonNode.Parse(content)?.AsObject()
                ?? throw new InvalidOperationException("The import response was invalid.");
            var retention = new StatementRetentionResult(
                false,
                null,
                null,
                "The original statement was not retained.");

            if (request.RetainOriginal)
            {
                if (!TryGetUserId(out var userId) ||
                    !result.TryGetPropertyValue("importId", out var importNode) ||
                    !Guid.TryParse(importNode?.GetValue<string>(), out var importRunId))
                {
                    throw new InvalidOperationException("The retained statement could not be linked to its import.");
                }

                try
                {
                    retention = await _retentionService.RetainAsync(
                        request.File,
                        userId,
                        importRunId,
                        parsed.FileChecksum,
                        request.RetentionDays,
                        Request.Headers.Authorization.FirstOrDefault() ?? "",
                        cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    retention = new StatementRetentionResult(false, null, null, ex.Message);
                }
            }

            result["retention"] = JsonSerializer.SerializeToNode(retention);
            result["reconciliation"] = JsonSerializer.SerializeToNode(parsed.Reconciliation);
            return Content(result.ToJsonString(), "application/json");
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            return UnprocessableEntity(new { Message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { Message = "The statement import failed. No partial import was kept." });
        }
    }

    [HttpDelete("{importRunId:guid}/statement")]
    public async Task<IActionResult> DeleteRetainedStatement(
        Guid importRunId,
        CancellationToken cancellationToken)
    {
        var authorization = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authorization))
            return Unauthorized(new { Message = "Authentication is required." });

        try
        {
            await _retentionService.DeleteAsync(importRunId, authorization, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { Message = "No retained statement exists for this import." });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { Message = ex.Message });
        }
    }

    private string? ValidateRequest(StatementImportRequest request)
    {
        if (request.File == null)
            return "A statement file is required.";

        if (NormalizeSourceType(request.SourceType) == "")
            return "Source type must be bank or card.";

        if (string.IsNullOrWhiteSpace(request.AccountName) || request.AccountName.Trim().Length > 80)
            return "Account name is required and must be 80 characters or fewer.";

        var masked = NormalizeMaskedIdentifier(request.MaskedIdentifier);
        if (masked?.Length > 32)
            return "Masked identifier must be 32 characters or fewer.";
        if (masked != null && System.Text.RegularExpressions.Regex.IsMatch(masked, "\\d{5,}"))
            return "Store only a masked identifier or the last four digits.";

        if (request.RetainOriginal && request.RetentionDays is not (30 or 90))
            return "Original statements can be retained for 30 or 90 days.";

        return null;
    }

    private HttpRequestMessage CreateRpcRequest(object payload)
    {
        var supabaseUrl = _configuration["Supabase:Url"];
        var anonKey = _configuration["Supabase:AnonKey"];
        var authorization = Request.Headers.Authorization.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(supabaseUrl)
            || string.IsNullOrWhiteSpace(anonKey)
            || string.IsNullOrWhiteSpace(authorization))
        {
            throw new InvalidOperationException("Supabase import configuration is incomplete.");
        }

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{supabaseUrl.TrimEnd('/')}/rest/v1/rpc/import_financehub_statement");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        request.Headers.Add("apikey", anonKey);
        request.Content = JsonContent.Create(payload);
        return request;
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }

    private static string NormalizeSourceType(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "bank" => "bank",
            "card" or "credit_card" => "credit_card",
            _ => ""
        };
    }

    private static string? NormalizeMaskedIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateIdempotencyKey(
        string checksum,
        string sourceType,
        string accountName,
        string? maskedIdentifier)
    {
        var value = string.Join(
            "|",
            checksum,
            sourceType,
            accountName.Trim().ToLowerInvariant(),
            NormalizeMaskedIdentifier(maskedIdentifier)?.ToLowerInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

public sealed class StatementImportRequest
{
    public IFormFile File { get; set; } = null!;
    public string SourceType { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string? MaskedIdentifier { get; set; }
    public string? Password { get; set; }
    public bool ConfirmReconciliationMismatch { get; set; }
    public bool RetainOriginal { get; set; }
    public int RetentionDays { get; set; } = 30;
}

public sealed record StatementPreviewResponse(
    string Parser,
    string ParserVersion,
    string FileType,
    string FileChecksum,
    int TransactionCount,
    int ReviewCount,
    IReadOnlyList<string> Errors,
    IReadOnlyList<StatementTransaction> Transactions,
    StatementReconciliation Reconciliation,
    bool UsedOcr);
