using FamilyLedger.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FamilyLedger.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class BudgetsController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public BudgetsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private Postgrest.Client GetPostgrestClient()
        {
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                throw new UnauthorizedAccessException("Missing Authorization header.");

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var options = new Postgrest.ClientOptions 
            { 
                Headers = new Dictionary<string, string> 
                { 
                    { "Authorization", $"Bearer {token}" },
                    { "apikey", _configuration["Supabase:AnonKey"]! }
                } 
            };
            return new Postgrest.Client($"{_configuration["Supabase:Url"]}/rest/v1", options);
        }

        [HttpGet]
        public async Task<IActionResult> GetBudgets([FromQuery] string? month)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                var query = postgrest.Table<Budget>();
                
                if (!string.IsNullOrEmpty(month))
                {
                    query = query.Filter("month", Postgrest.Constants.Operator.Equals, month);
                }

                var response = await query.Get();
                return Ok(response.Models);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateBudget([FromBody] CreateBudgetRequest request)
        {
            try
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                {
                    return Unauthorized(new { Message = "User ID not found in token." });
                }

                var newBudget = new Budget
                {
                    UserId = userId,
                    CategoryId = request.CategoryId,
                    Month = request.Month,
                    Amount = request.Amount
                };

                var postgrest = GetPostgrestClient();
                var existingResponse = await postgrest.Table<Budget>()
                    .Filter("category_id", Postgrest.Constants.Operator.Equals, request.CategoryId.ToString())
                    .Filter("month", Postgrest.Constants.Operator.Equals, request.Month)
                    .Get();
                
                if (existingResponse.Models.Any())
                {
                    return BadRequest(new { Message = "Budget for this category and month already exists." });
                }

                var response = await postgrest.Table<Budget>().Insert(newBudget);
                return Ok(response.Models.FirstOrDefault());
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateBudget(Guid id, [FromBody] UpdateBudgetRequest request)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                var res = await postgrest.Table<Budget>().Where(x => x.Id == id).Get();
                var budget = res.Models.FirstOrDefault();
                
                if (budget == null) return NotFound(new { Message = "Budget not found." });

                if (request.Amount.HasValue) budget.Amount = request.Amount.Value;

                var updateResponse = await postgrest.Table<Budget>().Update(budget);
                return Ok(updateResponse.Models.FirstOrDefault());
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBudget(Guid id)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                await postgrest.Table<Budget>().Where(x => x.Id == id).Delete();
                return Ok(new { Message = "Budget deleted successfully." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }
    }

    public class CreateBudgetRequest
    {
        public Guid CategoryId { get; set; }
        public string Month { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class UpdateBudgetRequest
    {
        public decimal? Amount { get; set; }
    }
}
