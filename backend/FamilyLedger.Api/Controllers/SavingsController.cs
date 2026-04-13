using FamilyLedger.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FamilyLedger.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class SavingsController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public SavingsController(IConfiguration configuration)
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
        public async Task<IActionResult> GetSavingsGoals()
        {
            try
            {
                var postgrest = GetPostgrestClient();
                var response = await postgrest.Table<SavingsGoal>().Get();
                return Ok(response.Models.OrderByDescending(s => s.CreatedAt));
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateSavingsGoal([FromBody] CreateSavingsRequest request)
        {
            try
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
                if (!Guid.TryParse(userIdString, out var userId)) return Unauthorized();

                var newGoal = new SavingsGoal
                {
                    UserId = userId,
                    Name = request.Name,
                    TargetAmount = request.TargetAmount,
                    CurrentAmount = request.CurrentAmount,
                    Deadline = request.Deadline,
                    IsCompleted = false
                };

                var postgrest = GetPostgrestClient();
                var response = await postgrest.Table<SavingsGoal>().Insert(newGoal);
                return Ok(response.Models.FirstOrDefault());
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateSavingsGoal(Guid id, [FromBody] UpdateSavingsRequest request)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                var res = await postgrest.Table<SavingsGoal>().Where(x => x.Id == id).Get();
                var goal = res.Models.FirstOrDefault();
                
                if (goal == null) return NotFound(new { Message = "Goal not found"});

                if (request.Name != null) goal.Name = request.Name;
                if (request.TargetAmount.HasValue) goal.TargetAmount = request.TargetAmount.Value;
                if (request.CurrentAmount.HasValue) goal.CurrentAmount = request.CurrentAmount.Value;
                if (request.Deadline.HasValue) goal.Deadline = request.Deadline.Value;
                if (request.IsCompleted.HasValue) goal.IsCompleted = request.IsCompleted.Value;

                var updateResponse = await postgrest.Table<SavingsGoal>().Update(goal);
                return Ok(updateResponse.Models.FirstOrDefault());
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSavingsGoal(Guid id)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                await postgrest.Table<SavingsGoal>().Where(x => x.Id == id).Delete();
                return Ok(new { Message = "Goal deleted" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }
    }

    public class CreateSavingsRequest
    {
        public string Name { get; set; } = string.Empty;
        public decimal TargetAmount { get; set; }
        public decimal CurrentAmount { get; set; }
        public DateTime? Deadline { get; set; }
    }

    public class UpdateSavingsRequest
    {
        public string? Name { get; set; }
        public decimal? TargetAmount { get; set; }
        public decimal? CurrentAmount { get; set; }
        public DateTime? Deadline { get; set; }
        public bool? IsCompleted { get; set; }
    }
}
