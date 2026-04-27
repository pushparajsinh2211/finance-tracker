using FamilyLedger.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyLedger.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationsController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public NotificationsController(IConfiguration configuration)
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
        public async Task<IActionResult> GetNotifications()
        {
            try
            {
                var postgrest = GetPostgrestClient();
                // RLS limits it to user_id automatically
                var response = await postgrest.Table<Notification>()
                    .Order("created_at", Postgrest.Constants.Ordering.Descending)
                    .Get();
                
                var dtos = response.Models.Select(n => new NotificationDto
                {
                    Id = n.Id,
                    UserId = n.UserId,
                    Message = n.Message,
                    IsRead = n.IsRead,
                    CreatedAt = n.CreatedAt
                }).ToList();

                return Ok(dtos);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPatch("{id}/read")]
        public async Task<IActionResult> MarkAsRead(Guid id)
        {
            try
            {
                var postgrest = GetPostgrestClient();
                var res = await postgrest.Table<Notification>().Where(x => x.Id == id).Get();
                var notif = res.Models.FirstOrDefault();
                
                if (notif == null) return NotFound(new { Message = "Notification not found" });

                notif.IsRead = true;
                await postgrest.Table<Notification>().Update(notif);
                
                return Ok(new NotificationDto
                {
                    Id = notif.Id,
                    UserId = notif.UserId,
                    Message = notif.Message,
                    IsRead = notif.IsRead,
                    CreatedAt = notif.CreatedAt
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }
        
        [HttpPatch("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            try
            {
                var postgrest = GetPostgrestClient();
                var unreadRsp = await postgrest.Table<Notification>().Filter("is_read", Postgrest.Constants.Operator.Equals, "false").Get();
                
                foreach (var model in unreadRsp.Models)
                {
                    model.IsRead = true;
                    await postgrest.Table<Notification>().Update(model);
                }
                return Ok(new { Message = "All marked as read" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }
    }
}
