using System.Security.Claims;
using FamilyLedger.Api.Models;
using FamilyLedger.Api.Services.FinanceHub;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyLedger.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/financehub/sources")]
public sealed class FinanceHubSourcesController : ControllerBase
{
    private readonly IFinancialDataProvider _provider;

    private static readonly IReadOnlyList<FinanceHubSource> Sources =
    [
        new(
            "bank",
            "Bank accounts",
            "CSV and text-based PDF statement import is available. Automatic sync still requires a regulated Account Aggregator provider.",
            "import_available",
            false,
            ["CSV/PDF import", "UPI detection", "deduplication", "automatic sync after provider onboarding"]),
        new(
            "card",
            "Credit cards",
            "Issuer statement import is available. Automatic coverage depends on a future provider and issuer support.",
            "import_available",
            false,
            ["CSV/PDF import", "refund detection", "deduplication", "review queue"]),
        new(
            "upi",
            "UPI",
            "UPI activity is detected from imported bank transaction narration and references.",
            "derived_available",
            false,
            ["transaction detection", "UTR/RRN capture", "deduplication"]),
        new(
            "cash",
            "Cash",
            "Manual cash expenses continue through the existing expense workflow.",
            "available",
            false,
            ["manual entry", "receipts", "recurring expenses"])
    ];

    public FinanceHubSourcesController(IEnumerable<IFinancialDataProvider> providers)
    {
        _provider = providers.First();
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FinanceHubSource>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<FinanceHubSource>> GetSources() => Ok(Sources);

    [HttpPost("{sourceId}/connect")]
    [ProducesResponseType<FinanceHubConnectionResult>(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FinanceHubConnectionResult>> Connect(
        string sourceId,
        [FromBody] ConnectFinanceHubSourceRequest request,
        CancellationToken cancellationToken)
    {
        var source = Sources.FirstOrDefault(item =>
            string.Equals(item.Id, sourceId, StringComparison.OrdinalIgnoreCase));

        if (source is null)
            return BadRequest(new { Message = "Unknown FinanceHub source." });

        if (source.Id is "cash" or "upi")
        {
            return StatusCode(
                StatusCodes.Status501NotImplemented,
                new FinanceHubConnectionResult(
                    source.Id,
                    "no_provider_connection",
                    source.Id == "cash"
                        ? "Cash uses the manual expense flow."
                        : "UPI activity is derived from imported or future linked bank transactions."));
        }

        if (!request.ConsentAcknowledged)
            return BadRequest(new { Message = "Consent acknowledgement is required before starting a provider flow." });

        if (!TryGetUserId(out var userId))
            return Unauthorized(new { Message = "User ID not found in token." });

        var result = await _provider.StartConnectionAsync(
            source.Id,
            userId,
            request.RedirectUrl,
            cancellationToken);

        return StatusCode(
            _provider.IsConfigured
                ? StatusCodes.Status200OK
                : StatusCodes.Status501NotImplemented,
            new FinanceHubConnectionResult(source.Id, result.Status, result.Message));
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}
