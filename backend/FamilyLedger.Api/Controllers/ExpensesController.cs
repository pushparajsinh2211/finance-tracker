using FamilyLedger.Api.Infrastructure;
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
        private const int MaxNoteLength = 200;
        private readonly IConfiguration _configuration;

        public ExpensesController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private Postgrest.Client GetPostgrestClient()
        {
            return AuthenticatedPostgrestClientFactory.Create(
                _configuration,
                Request.Headers.Authorization.FirstOrDefault());
        }

        [HttpGet]
        public async Task<IActionResult> GetExpenses([FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, [FromQuery] Guid? categoryId)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                var query = postgrest.Table<Expense>().Filter("deleted_at", Postgrest.Constants.Operator.Is, "null");

                if (startDate.HasValue)
                    query = query.Filter("date", Postgrest.Constants.Operator.GreaterThanOrEqual, startDate.Value.ToString("yyyy-MM-dd"));

                if (endDate.HasValue)
                    query = query.Filter("date", Postgrest.Constants.Operator.LessThanOrEqual, endDate.Value.ToString("yyyy-MM-dd"));

                if (categoryId.HasValue)
                    query = query.Filter("category_id", Postgrest.Constants.Operator.Equals, categoryId.Value.ToString());

                var response = await query.Get();
                return Ok(response.Models
                    .Select(ToDto)
                    .OrderByDescending(expense => expense.Date)
                    .ToList());
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddExpense([FromBody] CreateExpenseRequest request)
        {
            if (!TryGetCurrentUserId(out var userId))
                return Unauthorized(new { Message = "User ID not found in token." });

            var validationMessage = ValidateExpenseFields(request.Amount, request.Note, request.ReceiptUrl, userId);
            if (validationMessage != null)
                return BadRequest(new { Message = validationMessage });

            try
            {
                var postgrest = GetPostgrestClient();
                var membership = await GetCurrentMembership(postgrest, userId);
                if (membership == null)
                    return BadRequest(new { Message = "User is not a member of any family." });

                if (!await CategoryBelongsToFamily(postgrest, request.CategoryId, membership.FamilyId))
                    return BadRequest(new { Message = "Category does not belong to your family." });

                var newExpense = new Expense
                {
                    UserId = userId,
                    FamilyId = membership.FamilyId,
                    CategoryId = request.CategoryId,
                    Amount = request.Amount,
                    Date = (request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow))
                        .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    Note = NormalizeNote(request.Note),
                    IsRecurring = request.IsRecurring,
                    ReceiptUrl = NormalizeReceiptPath(request.ReceiptUrl)
                };

                var response = await postgrest.Table<Expense>().Insert(newExpense);
                var created = response.Models.FirstOrDefault();
                return created == null
                    ? BadRequest(new { Message = "Expense could not be created." })
                    : Ok(ToDto(created));
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> EditExpense(Guid id, [FromBody] UpdateExpenseRequest request)
        {
            if (!TryGetCurrentUserId(out var userId))
                return Unauthorized(new { Message = "User ID not found in token." });

            if (request.Amount.HasValue && request.Amount.Value <= 0)
                return BadRequest(new { Message = "Amount must be greater than zero." });

            if (request.Note != null && request.Note.Length > MaxNoteLength)
                return BadRequest(new { Message = $"Note must be {MaxNoteLength} characters or fewer." });

            if (!request.RemoveReceipt && request.ReceiptUrl != null && !IsOwnedReceiptPath(request.ReceiptUrl, userId))
                return BadRequest(new { Message = "Receipt path is invalid." });

            try
            {
                var postgrest = GetPostgrestClient();
                var membership = await GetCurrentMembership(postgrest, userId);
                if (membership == null)
                    return BadRequest(new { Message = "User is not a member of any family." });

                var expenseResponse = await postgrest.Table<Expense>().Where(expense => expense.Id == id).Get();
                var expense = expenseResponse.Models.FirstOrDefault();
                if (expense == null || expense.DeletedAt != null)
                    return NotFound(new { Message = "Expense not found." });

                if (request.CategoryId.HasValue)
                {
                    if (!await CategoryBelongsToFamily(postgrest, request.CategoryId.Value, membership.FamilyId))
                        return BadRequest(new { Message = "Category does not belong to your family." });

                    expense.CategoryId = request.CategoryId.Value;
                }

                if (request.Amount.HasValue) expense.Amount = request.Amount.Value;
                if (request.Date.HasValue)
                    expense.Date = request.Date.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                if (request.Note != null) expense.Note = NormalizeNote(request.Note);
                if (request.IsRecurring.HasValue) expense.IsRecurring = request.IsRecurring.Value;

                if (request.RemoveReceipt)
                    expense.ReceiptUrl = null;
                else if (request.ReceiptUrl != null)
                    expense.ReceiptUrl = NormalizeReceiptPath(request.ReceiptUrl);

                await postgrest.Table<Expense>().Update(expense);
                return Ok(ToDto(expense));
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
                var postgrest = GetPostgrestClient();
                var expenseResponse = await postgrest.Table<Expense>().Where(expense => expense.Id == id).Get();
                var expense = expenseResponse.Models.FirstOrDefault();
                if (expense == null || expense.DeletedAt != null)
                    return NotFound(new { Message = "Expense not found." });

                expense.DeletedAt = DateTime.UtcNow;
                await postgrest.Table<Expense>().Update(expense);

                return Ok(new { Message = "Expense soft-deleted." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        private bool TryGetCurrentUserId(out Guid userId)
        {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return Guid.TryParse(value, out userId);
        }

        private static async Task<FamilyMember?> GetCurrentMembership(Postgrest.Client postgrest, Guid userId)
        {
            var response = await postgrest.Table<FamilyMember>().Where(member => member.UserId == userId).Get();
            return response.Models.FirstOrDefault();
        }

        private static async Task<bool> CategoryBelongsToFamily(Postgrest.Client postgrest, Guid categoryId, Guid familyId)
        {
            if (categoryId == Guid.Empty)
                return false;

            var response = await postgrest.Table<Category>().Where(category => category.Id == categoryId).Get();
            return response.Models.Any(category => category.FamilyId == familyId && !category.IsArchived);
        }

        private static string? ValidateExpenseFields(decimal amount, string? note, string? receiptPath, Guid userId)
        {
            if (amount <= 0)
                return "Amount must be greater than zero.";

            if (note != null && note.Length > MaxNoteLength)
                return $"Note must be {MaxNoteLength} characters or fewer.";

            if (receiptPath != null && !IsOwnedReceiptPath(receiptPath, userId))
                return "Receipt path is invalid.";

            return null;
        }

        private static bool IsOwnedReceiptPath(string receiptPath, Guid userId)
        {
            if (string.IsNullOrWhiteSpace(receiptPath)
                || receiptPath.Contains("..", StringComparison.Ordinal)
                || receiptPath.Contains('\\')
                || Uri.IsWellFormedUriString(receiptPath, UriKind.Absolute))
            {
                return false;
            }

            return receiptPath.StartsWith(userId.ToString() + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizeNote(string? note)
        {
            return string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        }

        private static string? NormalizeReceiptPath(string? receiptPath)
        {
            return string.IsNullOrWhiteSpace(receiptPath) ? null : receiptPath.Trim();
        }

        private static ExpenseDto ToDto(Expense expense)
        {
            return new ExpenseDto
            {
                Id = expense.Id,
                FamilyId = expense.FamilyId,
                CategoryId = expense.CategoryId,
                MemberUserId = expense.UserId,
                Amount = expense.Amount,
                Note = expense.Note ?? string.Empty,
                Date = DateOnly.FromDateTime(expense.Date),
                IsRecurring = expense.IsRecurring,
                ReceiptUrl = expense.ReceiptUrl ?? string.Empty
            };
        }
    }

    public class CreateExpenseRequest
    {
        public Guid CategoryId { get; set; }
        public decimal Amount { get; set; }
        public DateOnly? Date { get; set; }
        public string? Note { get; set; }
        public bool IsRecurring { get; set; }
        public string? ReceiptUrl { get; set; }
    }

    public class UpdateExpenseRequest
    {
        public Guid? CategoryId { get; set; }
        public decimal? Amount { get; set; }
        public DateOnly? Date { get; set; }
        public string? Note { get; set; }
        public bool? IsRecurring { get; set; }
        public string? ReceiptUrl { get; set; }
        public bool RemoveReceipt { get; set; }
    }
}
