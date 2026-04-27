using FamilyLedger.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FamilyLedger.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ExpensesController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public ExpensesController(IConfiguration configuration)
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
        public async Task<IActionResult> GetExpenses([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] Guid? categoryId)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                // RLS automatically limits to the user's own expenses.
                var query = postgrest.Table<Expense>().Filter("deleted_at", Postgrest.Constants.Operator.Is, "null");

                if (startDate.HasValue)
                    query = query.Filter("date", Postgrest.Constants.Operator.GreaterThanOrEqual, startDate.Value.ToString("yyyy-MM-dd"));
                
                if (endDate.HasValue)
                    query = query.Filter("date", Postgrest.Constants.Operator.LessThanOrEqual, endDate.Value.ToString("yyyy-MM-dd"));
                
                if (categoryId.HasValue)
                    query = query.Filter("category_id", Postgrest.Constants.Operator.Equals, categoryId.Value.ToString());

                var response = await query.Get();
                var dtos = response.Models.Select(e => new ExpenseDto
                {
                    Id = e.Id,
                    FamilyId = e.FamilyId,
                    CategoryId = e.CategoryId,
                    MemberUserId = e.UserId,
                    Amount = e.Amount,
                    Note = e.Note ?? string.Empty,
                    Date = e.Date
                }).OrderByDescending(e => e.Date).ToList();

                return Ok(dtos);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddExpense([FromBody] CreateExpenseRequest request)
        {
            try
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                {
                    return Unauthorized(new { Message = "User ID not found in token." });
                }

                var postgrest = GetPostgrestClient();
                var familyMemberResponse = await postgrest.Table<FamilyMember>().Where(f => f.UserId == userId).Get();
                var membership = familyMemberResponse.Models.FirstOrDefault();

                if (membership == null)
                    return BadRequest(new { Message = "User is not a member of any family." });

                var newExpense = new Expense
                {
                    UserId = userId,
                    FamilyId = membership.FamilyId,
                    CategoryId = request.CategoryId,
                    Amount = request.Amount,
                    Date = request.Date ?? DateTime.UtcNow.Date,
                    Note = request.Note,
                    IsRecurring = request.IsRecurring,
                    ReceiptUrl = request.ReceiptUrl
                };

                var response = await postgrest.Table<Expense>().Insert(newExpense);
                var created = response.Models.FirstOrDefault();

                if (created == null) return BadRequest();

                return Ok(new ExpenseDto
                {
                    Id = created.Id,
                    FamilyId = created.FamilyId,
                    CategoryId = created.CategoryId,
                    MemberUserId = created.UserId,
                    Amount = created.Amount,
                    Note = created.Note ?? string.Empty,
                    Date = created.Date
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> EditExpense(Guid id, [FromBody] UpdateExpenseRequest request)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                
                var expenseResponse = await postgrest.Table<Expense>().Where(e => e.Id == id).Get();
                var expense = expenseResponse.Models.FirstOrDefault();
                if (expense == null || expense.DeletedAt != null)
                    return NotFound(new { Message = "Expense not found." });

                if (request.CategoryId.HasValue) expense.CategoryId = request.CategoryId.Value;
                if (request.Amount.HasValue) expense.Amount = request.Amount.Value;
                if (request.Date.HasValue) expense.Date = request.Date.Value;
                if (request.Note != null) expense.Note = request.Note;
                if (request.IsRecurring.HasValue) expense.IsRecurring = request.IsRecurring.Value;
                if (request.ReceiptUrl != null) expense.ReceiptUrl = request.ReceiptUrl;

                await postgrest.Table<Expense>().Update(expense);
                
                return Ok(new ExpenseDto
                {
                    Id = expense.Id,
                    FamilyId = expense.FamilyId,
                    CategoryId = expense.CategoryId,
                    MemberUserId = expense.UserId,
                    Amount = expense.Amount,
                    Note = expense.Note ?? string.Empty,
                    Date = expense.Date
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteExpense(Guid id)
        {
            try
            {
                // Soft delete
                var postgrest = GetPostgrestClient();
                var expenseResponse = await postgrest.Table<Expense>().Where(e => e.Id == id).Get();
                var expense = expenseResponse.Models.FirstOrDefault();
                if (expense == null) return NotFound(new { Message = "Expense not found." });

                expense.DeletedAt = DateTime.UtcNow;
                await postgrest.Table<Expense>().Update(expense);

                return Ok(new { Message = "Expense soft-deleted." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }
    }

    public class CreateExpenseRequest
    {
        public Guid CategoryId { get; set; }
        public decimal Amount { get; set; }
        public DateTime? Date { get; set; }
        public string? Note { get; set; }
        public bool IsRecurring { get; set; }
        public string? ReceiptUrl { get; set; }
    }

    public class UpdateExpenseRequest
    {
        public Guid? CategoryId { get; set; }
        public decimal? Amount { get; set; }
        public DateTime? Date { get; set; }
        public string? Note { get; set; }
        public bool? IsRecurring { get; set; }
        public string? ReceiptUrl { get; set; }
    }
}
