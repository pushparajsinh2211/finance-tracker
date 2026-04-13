using FamilyLedger.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FamilyLedger.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class EmisController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public EmisController(IConfiguration configuration)
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
        public async Task<IActionResult> GetEmis()
        {
            try
            {
                var postgrest = GetPostgrestClient();
                var response = await postgrest.Table<Emi>().Get();
                return Ok(response.Models.OrderByDescending(e => e.CreatedAt));
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateEmi([FromBody] CreateEmiRequest request)
        {
            try
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
                if (!Guid.TryParse(userIdString, out var userId)) return Unauthorized();

                var newEmi = new Emi
                {
                    UserId = userId,
                    LenderName = request.LenderName,
                    Principal = request.Principal,
                    MonthlyEmi = request.MonthlyEmi,
                    StartDate = request.StartDate,
                    TenureMonths = request.TenureMonths
                };

                var postgrest = GetPostgrestClient();
                var response = await postgrest.Table<Emi>().Insert(newEmi);
                return Ok(response.Models.FirstOrDefault());
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> UpdateEmi(Guid id, [FromBody] UpdateEmiRequest request)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                var res = await postgrest.Table<Emi>().Where(x => x.Id == id).Get();
                var emi = res.Models.FirstOrDefault();
                
                if (emi == null) return NotFound(new { Message = "EMI not found"});

                if (request.LenderName != null) emi.LenderName = request.LenderName;
                if (request.Principal.HasValue) emi.Principal = request.Principal.Value;
                if (request.MonthlyEmi.HasValue) emi.MonthlyEmi = request.MonthlyEmi.Value;
                if (request.StartDate.HasValue) emi.StartDate = request.StartDate.Value;
                if (request.TenureMonths.HasValue) emi.TenureMonths = request.TenureMonths.Value;

                var updateResponse = await postgrest.Table<Emi>().Update(emi);
                return Ok(updateResponse.Models.FirstOrDefault());
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteEmi(Guid id)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                await postgrest.Table<Emi>().Where(x => x.Id == id).Delete();
                return Ok(new { Message = "EMI deleted" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }
    }

    public class CreateEmiRequest
    {
        public string LenderName { get; set; } = string.Empty;
        public decimal Principal { get; set; }
        public decimal MonthlyEmi { get; set; }
        public DateTime StartDate { get; set; }
        public int TenureMonths { get; set; }
    }

    public class UpdateEmiRequest
    {
        public string? LenderName { get; set; }
        public decimal? Principal { get; set; }
        public decimal? MonthlyEmi { get; set; }
        public DateTime? StartDate { get; set; }
        public int? TenureMonths { get; set; }
    }
}
