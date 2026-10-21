using FamilyLedger.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyLedger.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/financehub/sources")]
public sealed class FinanceHubSourcesController : ControllerBase
{
    private static readonly IReadOnlyList<FinanceHubSource> Sources =
    [
        new(
            "bank",
            "Bank accounts",
            "Automatic sync through a contracted, regulated Account Aggregator/provider.",
            "planned",
            true,
            ["balances", "transactions", "consent", "revocation"]),
        new(
            "card",
            "Credit cards",
            "Provider coverage or reviewed issuer-statement imports; coverage varies by issuer.",
            "planned",
            true,
            ["transactions", "statements", "refunds", "bill-payment linking"]),
        new(
            "upi",
            "UPI",
            "Detected primarily from linked bank transactions using UTR/RRN and narration.",
            "planned",
            true,
            ["transaction detection", "deduplication"]),
        new(
            "cash",
            "Cash",
            "Manual cash expenses continue through the existing expense workflow.",
            "available",
            false,
            ["manual entry", "receipts", "recurring expenses"])
    ];

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FinanceHubSource>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<FinanceHubSource>> GetSources() => Ok(Sources);

    [HttpPost("{sourceId}/connect")]
    [ProducesResponseType<FinanceHubConnectionResult>(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<FinanceHubConnectionResult> Connect(
        string sourceId,
        [FromBody] ConnectFinanceHubSourceRequest request)
    {
        var source = Sources.FirstOrDefault(item =>
            string.Equals(item.Id, sourceId, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            return BadRequest(new { Message = "Unknown FinanceHub source." });
        }

        if (source.Id == "cash")
        {
            return StatusCode(
                StatusCodes.Status501NotImplemented,
                new FinanceHubConnectionResult(
                    source.Id,
                    "use_manual_entry",
                    "Cash does not require a provider connection. Use the existing expense entry flow."));
        }

        if (!request.ConsentAcknowledged)
        {
            return BadRequest(new { Message = "Consent acknowledgement is required before starting a provider flow." });
        }

        return StatusCode(
            StatusCodes.Status501NotImplemented,
            new FinanceHubConnectionResult(
                source.Id,
                "planned",
                "No production provider is configured. Select and onboard a regulated provider before enabling this flow."));
    }
}
