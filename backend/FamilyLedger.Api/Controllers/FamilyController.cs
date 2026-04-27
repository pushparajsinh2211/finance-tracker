using FamilyLedger.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FamilyLedger.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class FamilyController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public FamilyController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private Postgrest.Client GetPostgrestClient()
        {
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                throw new UnauthorizedAccessException("Missing or invalid Authorization header.");
            }
            var token = authHeader.Substring("Bearer ".Length).Trim();
            
            var url = _configuration["Supabase:Url"];
            var key = _configuration["Supabase:AnonKey"];

            var options = new Postgrest.ClientOptions 
            { 
                Headers = new Dictionary<string, string> 
                { 
                    { "Authorization", $"Bearer {token}" },
                    { "apikey", key! }
                } 
            };
            return new Postgrest.Client($"{url}/rest/v1", options);
        }

        [HttpPost]
        public async Task<IActionResult> CreateFamily([FromBody] CreateFamilyRequest request)
        {
            try
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                {
                    return Unauthorized(new { Message = "User ID not found in token." });
                }

                var postgrest = GetPostgrestClient();
                
                string inviteCode = GenerateInviteCode();

                var newFamily = new Family
                {
                    Name = request.Name,
                    HeadUserId = userId,
                    InviteCode = inviteCode,
                    InviteActive = true
                };

                var familyResponse = await postgrest.Table<Family>().Insert(newFamily);
                var createdFamily = familyResponse.Models.FirstOrDefault();

                if (createdFamily == null)
                    return BadRequest(new { Message = "Failed to create family." });

                var newMember = new FamilyMember
                {
                    FamilyId = createdFamily.Id,
                    UserId = userId,
                    DisplayName = "Head",
                    Relation = "Head",
                    IsDependent = false
                };

                await postgrest.Table<FamilyMember>().Insert(newMember);

                var defaultCategories = new List<string> { 
                    "Groceries", "Rent", "EMI / Loan", "Medical", 
                    "Education", "Transport", "Utilities", "Entertainment", 
                    "Dining Out", "Clothing", "Miscellaneous" 
                };
                var colors = new[] { "#4caf50", "#2196f3", "#f44336", "#9c27b0", "#ff9800", "#9e9e9e", "#ffeb3b", "#e91e63", "#795548", "#00bcd4", "#009688" };

                var categoriesToInsert = defaultCategories.Select((name, idx) => new Category 
                {
                    FamilyId = createdFamily.Id,
                    Name = name,
                    Color = colors[idx],
                    IsDefault = true,
                    IsArchived = false
                }).ToList();

                await postgrest.Table<Category>().Insert(categoriesToInsert);

                return Ok(createdFamily);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        private string GenerateInviteCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        [HttpPost("join")]
        public async Task<IActionResult> JoinFamily([FromBody] JoinFamilyRequest request)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                var rpcResult = await postgrest.Rpc("join_family_by_code", new Dictionary<string, object>
                {
                    { "p_invite_code", request.InviteCode },
                    { "p_display_name", string.IsNullOrWhiteSpace(request.DisplayName) ? "Member" : request.DisplayName }
                });

                return Ok(new { Message = "Successfully joined family." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetFamily()
        {
            try
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                    return Unauthorized("User ID not found.");

                var postgrest = GetPostgrestClient();
                var fmResp = await postgrest.Table<FamilyMember>().Where(f => f.UserId == userId).Get();
                var membership = fmResp.Models.FirstOrDefault();

                if (membership == null) return NotFound(new { Message = "User is not a member of any family." });

                var familyResp = await postgrest.Table<Family>().Where(f => f.Id == membership.FamilyId).Get();
                return Ok(familyResp.Models.FirstOrDefault());
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpGet("members")]
        public async Task<IActionResult> GetMembers()
        {
            try
            {
                var postgrest = GetPostgrestClient();
                var response = await postgrest.Table<FamilyMember>().Get();
                return Ok(response.Models);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPatch("members/{id}/toggle")]
        public async Task<IActionResult> ToggleDependent(Guid id)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                var memberResponse = await postgrest.Table<FamilyMember>().Where(x => x.Id == id).Get();
                var member = memberResponse.Models.FirstOrDefault();

                if (member == null) return NotFound(new { Message = "Member not found." });

                member.IsDependent = !member.IsDependent;
                var updateResponse = await postgrest.Table<FamilyMember>().Update(member);

                return Ok(updateResponse.Models.FirstOrDefault());
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpDelete("members/{id}")]
        public async Task<IActionResult> RemoveMember(Guid id)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                await postgrest.Table<FamilyMember>().Where(x => x.Id == id).Delete();
                return Ok(new { Message = "Member removed." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetFamilySummary()
        {
            try
            {
                var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
                if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                {
                    return Unauthorized("User ID not found.");
                }

                var postgrest = GetPostgrestClient();
                var fmResp = await postgrest.Table<FamilyMember>().Where(f => f.UserId == userId).Get();
                var membership = fmResp.Models.FirstOrDefault();

                if (membership == null)
                    return BadRequest(new { Message = "User is not a member of any family." });

                var rpcResult = await postgrest.Rpc("get_dependent_expenses_summary", new Dictionary<string, object>
                {
                    { "target_family_id", membership.FamilyId }
                });

                return Content(rpcResult.Content ?? "[]", "application/json");
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }
    }

    public class CreateFamilyRequest
    {
        public string Name { get; set; } = string.Empty;
    }

    public class JoinFamilyRequest
    {
        public string InviteCode { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }
}
