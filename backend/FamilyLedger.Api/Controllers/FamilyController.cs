using FamilyLedger.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Mail;
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

        private bool TryGetCurrentUserId(out Guid userId)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return Guid.TryParse(userIdString, out userId);
        }

        private async Task<FamilyMember?> GetCurrentMembership(Postgrest.Client postgrest, Guid userId)
        {
            var fmResp = await postgrest.Table<FamilyMember>().Where(f => f.UserId == userId).Get();
            return fmResp.Models.FirstOrDefault();
        }

        private static bool IsHead(FamilyMember? member)
        {
            return string.Equals(member?.Relation, "Head", StringComparison.OrdinalIgnoreCase);
        }

        [HttpPost]
        public async Task<IActionResult> CreateFamily([FromBody] CreateFamilyRequest request)
        {
            try
            {
                if (!TryGetCurrentUserId(out var userId))
                {
                    return Unauthorized(new { Message = "User ID not found in token." });
                }

                var postgrest = GetPostgrestClient();
                var existingMembership = await GetCurrentMembership(postgrest, userId);
                if (existingMembership != null)
                {
                    return BadRequest(new { Message = "You already belong to a family. A user can be part of only one family." });
                }
                
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
                if (!TryGetCurrentUserId(out var userId))
                {
                    return Unauthorized(new { Message = "User ID not found in token." });
                }

                var postgrest = GetPostgrestClient();
                var existingMembership = await GetCurrentMembership(postgrest, userId);
                if (existingMembership != null)
                {
                    return BadRequest(new { Message = "You already belong to a family. A user can be part of only one family." });
                }

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
                if (!TryGetCurrentUserId(out var userId))
                    return Unauthorized("User ID not found.");

                var postgrest = GetPostgrestClient();
                var membership = await GetCurrentMembership(postgrest, userId);

                if (membership == null) return NotFound(new { Message = "User is not a member of any family." });

                var familyResp = await postgrest.Table<Family>().Where(f => f.Id == membership.FamilyId).Get();
                var family = familyResp.Models.FirstOrDefault();
                
                if (family == null) return NotFound();

                return Ok(new FamilyDto 
                { 
                    Id = family.Id, 
                    Name = family.Name, 
                    InviteCode = IsHead(membership) ? family.InviteCode : string.Empty,
                    HeadUserId = family.HeadUserId 
                });
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
                
                var dtos = response.Models.Select(m => new FamilyMemberDto
                {
                    Id = m.Id,
                    FamilyId = m.FamilyId,
                    UserId = m.UserId,
                    DisplayName = m.DisplayName,
                    Relation = m.Relation ?? string.Empty,
                    IsDependent = m.IsDependent
                }).ToList();

                return Ok(dtos);
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
                if (!TryGetCurrentUserId(out var userId))
                {
                    return Unauthorized(new { Message = "User ID not found in token." });
                }

                var currentMembership = await GetCurrentMembership(postgrest, userId);
                if (!IsHead(currentMembership))
                {
                    return Forbid();
                }

                var memberResponse = await postgrest.Table<FamilyMember>().Where(x => x.Id == id).Get();
                var member = memberResponse.Models.FirstOrDefault();

                if (member == null) return NotFound(new { Message = "Member not found." });
                if (member.FamilyId != currentMembership!.FamilyId)
                {
                    return NotFound(new { Message = "Member not found." });
                }
                if (IsHead(member))
                {
                    return BadRequest(new { Message = "The Family Head cannot be marked as dependent." });
                }

                member.IsDependent = !member.IsDependent;
                await postgrest.Table<FamilyMember>().Update(member);

                return Ok(new FamilyMemberDto
                {
                    Id = member.Id,
                    FamilyId = member.FamilyId,
                    UserId = member.UserId,
                    DisplayName = member.DisplayName,
                    Relation = member.Relation ?? string.Empty,
                    IsDependent = member.IsDependent
                });
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
                if (!TryGetCurrentUserId(out var userId))
                {
                    return Unauthorized(new { Message = "User ID not found in token." });
                }

                var currentMembership = await GetCurrentMembership(postgrest, userId);
                if (!IsHead(currentMembership))
                {
                    return Forbid();
                }

                var memberResponse = await postgrest.Table<FamilyMember>().Where(x => x.Id == id).Get();
                var member = memberResponse.Models.FirstOrDefault();

                if (member == null) return NotFound(new { Message = "Member not found." });
                if (member.FamilyId != currentMembership!.FamilyId)
                {
                    return NotFound(new { Message = "Member not found." });
                }
                if (IsHead(member))
                {
                    return BadRequest(new { Message = "The Family Head cannot be removed." });
                }

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
                if (!TryGetCurrentUserId(out var userId))
                {
                    return Unauthorized("User ID not found.");
                }

                var postgrest = GetPostgrestClient();
                var membership = await GetCurrentMembership(postgrest, userId);

                if (membership == null)
                    return BadRequest(new { Message = "User is not a member of any family." });
                if (!IsHead(membership))
                    return Forbid();

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
        [HttpPost("invite")]
        public async Task<IActionResult> InviteMember([FromBody] InviteMemberRequest request)
        {
            try
            {
                if (!MailAddress.TryCreate(request.Email, out var recipient))
                {
                    return BadRequest(new { Message = "Please provide a valid email address." });
                }

                if (!TryGetCurrentUserId(out var userId))
                    return Unauthorized("User ID not found.");

                var postgrest = GetPostgrestClient();
                
                // 1. Verify the current user is the head of the family
                var membership = await GetCurrentMembership(postgrest, userId);

                if (!IsHead(membership))
                    return Forbid();

                var headMembership = membership!;
                var familyResp = await postgrest.Table<Family>().Where(f => f.Id == headMembership.FamilyId).Get();
                var family = familyResp.Models.FirstOrDefault();

                if (family == null) return NotFound("Family not found.");

                if (!IsEmailConfigured())
                {
                    return StatusCode(StatusCodes.Status501NotImplemented, new
                    {
                        Message = "Email invitations are not configured yet. Copy and share the invite code instead.",
                        InviteCode = family.InviteCode
                    });
                }

                await SendInviteEmail(recipient, family);

                return Ok(new { Message = $"Invitation sent successfully to {request.Email}!" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        private bool IsEmailConfigured()
        {
            return !string.IsNullOrWhiteSpace(_configuration["Email:Smtp:Host"])
                && !string.IsNullOrWhiteSpace(_configuration["Email:From"]);
        }

        private async Task SendInviteEmail(MailAddress recipient, Family family)
        {
            var host = _configuration["Email:Smtp:Host"]!;
            var port = int.TryParse(_configuration["Email:Smtp:Port"], out var configuredPort) ? configuredPort : 587;
            var enableSsl = !bool.TryParse(_configuration["Email:Smtp:EnableSsl"], out var configuredSsl) || configuredSsl;
            var username = _configuration["Email:Smtp:Username"];
            var password = _configuration["Email:Smtp:Password"];
            var fromEmail = _configuration["Email:From"]!;
            var fromName = _configuration["Email:FromName"] ?? "FamilyLedger";

            using var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = $"You're invited to join {family.Name} on FamilyLedger",
                Body = $"""
                You've been invited to join {family.Name} on FamilyLedger.

                Use this invite code after signing in:
                {family.InviteCode}

                If you were not expecting this invitation, you can ignore this email.
                """,
                IsBodyHtml = false
            };
            message.To.Add(recipient);

            using var smtp = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl
            };

            if (!string.IsNullOrWhiteSpace(username))
            {
                smtp.Credentials = new NetworkCredential(username, password);
            }

            await smtp.SendMailAsync(message);
        }
    }

    public class InviteMemberRequest
    {
        public string Email { get; set; } = string.Empty;
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
