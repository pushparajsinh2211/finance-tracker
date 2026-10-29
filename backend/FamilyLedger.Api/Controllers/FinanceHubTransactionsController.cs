using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyLedger.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/financehub")]
public sealed class FinanceHubTransactionsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public FinanceHubTransactionsController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> ListTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] string? paymentRail = null,
        [FromQuery] string? direction = null,
        [FromQuery] bool? needsReview = null,
        [FromQuery] DateOnly? fromDate = null,
        [FromQuery] DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest(new { Message = "Page must be positive and page size must be between 1 and 100." });
        if (search?.Length > 120)
            return BadRequest(new { Message = "Search must be 120 characters or fewer." });
        if (direction is not null and not "" and not "debit" and not "credit")
            return BadRequest(new { Message = "Direction must be debit or credit." });
        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
            return BadRequest(new { Message = "From date cannot be after to date." });

        var payload = new
        {
            p_page = page,
            p_page_size = pageSize,
            p_search = Normalize(search),
            p_account_id = accountId,
            p_category_id = categoryId,
            p_payment_rail = Normalize(paymentRail)?.ToUpperInvariant(),
            p_direction = Normalize(direction)?.ToLowerInvariant(),
            p_needs_review = needsReview,
            p_from_date = fromDate,
            p_to_date = toDate
        };

        return await SendRpcAsync("list_financehub_transactions", payload, cancellationToken);
    }

    [HttpPatch("transactions/{id:guid}/review")]
    public async Task<IActionResult> ReviewTransaction(
        Guid id,
        [FromBody] ReviewFinanceHubTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            p_transaction_id = id,
            p_category_id = request.CategoryId,
            p_needs_review = request.NeedsReview
        };

        return await SendRpcAsync(
            "review_financehub_transaction",
            payload,
            cancellationToken,
            StatusCodes.Status422UnprocessableEntity);
    }

    [HttpGet("imports")]
    public async Task<IActionResult> ListImports(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 50)
            return BadRequest(new { Message = "Page must be positive and page size must be between 1 and 50." });

        return await SendRpcAsync(
            "list_financehub_imports",
            new { p_page = page, p_page_size = pageSize },
            cancellationToken);
    }

    private async Task<IActionResult> SendRpcAsync(
        string functionName,
        object payload,
        CancellationToken cancellationToken,
        int failureStatus = StatusCodes.Status502BadGateway)
    {
        try
        {
            using var message = CreateRpcRequest(functionName, payload);
            var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(message, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? StatusCodes.Status502BadGateway
                    : failureStatus;

                return StatusCode(
                    status,
                    new { Message = "The FinanceHub request could not be completed." });
            }

            return Content(content, "application/json");
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new { Message = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new { Message = "The FinanceHub service is temporarily unavailable." });
        }
    }

    private HttpRequestMessage CreateRpcRequest(string functionName, object payload)
    {
        var supabaseUrl = _configuration["Supabase:Url"];
        var anonKey = _configuration["Supabase:AnonKey"];
        var authorization = Request.Headers.Authorization.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(supabaseUrl)
            || string.IsNullOrWhiteSpace(anonKey)
            || string.IsNullOrWhiteSpace(authorization))
        {
            throw new InvalidOperationException("Supabase FinanceHub configuration is incomplete.");
        }

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{supabaseUrl.TrimEnd('/')}/rest/v1/rpc/{functionName}");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
        request.Headers.Add("apikey", anonKey);
        request.Content = JsonContent.Create(payload);
        return request;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ReviewFinanceHubTransactionRequest
{
    public Guid? CategoryId { get; set; }
    public bool NeedsReview { get; set; }
}
