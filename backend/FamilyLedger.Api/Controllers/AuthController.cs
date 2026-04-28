using Microsoft.AspNetCore.Mvc;
using Supabase.Gotrue;
using System.Net.Mail;

namespace FamilyLedger.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly Supabase.Client _supabaseClient;

        public AuthController(Supabase.Client supabaseClient)
        {
            _supabaseClient = supabaseClient;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                var session = await _supabaseClient.Auth.SignUp(request.Email, request.Password);
                if (session == null || string.IsNullOrEmpty(session?.AccessToken))
                {
                    return BadRequest(new { Message = "User created, but no login session started. Action Required: Please confirm your email or disable 'Confirm Email' in Supabase Authentication settings." });
                }
                return Ok(new AuthResponse(session));
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var session = await _supabaseClient.Auth.SignIn(request.Email, request.Password);
                if (session == null || string.IsNullOrEmpty(session?.AccessToken))
                {
                    return Unauthorized(new { Message = "Login failed: No access token returned. Ensure your email is confirmed." });
                }
                return Ok(new AuthResponse(session));
            }
            catch (Exception ex)
            {
                return Unauthorized(new { Message = ex.Message });
            }
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (!MailAddress.TryCreate(request.Email, out _))
            {
                return BadRequest(new { Message = "Please provide a valid email address." });
            }

            try
            {
                var redirectTo = string.IsNullOrWhiteSpace(request.RedirectTo) ? null : request.RedirectTo;
                if (redirectTo == null)
                {
                    await _supabaseClient.Auth.ResetPasswordForEmail(request.Email);
                }
                else
                {
                    await _supabaseClient.Auth.ResetPasswordForEmail(new ResetPasswordForEmailOptions(request.Email)
                    {
                        RedirectTo = redirectTo
                    });
                }

                return Ok(new { Message = "If an account exists, a reset link has been sent." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }
    }

    public class RegisterRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? RedirectTo { get; set; }
    }

    public class AuthResponse
    {
        public string? AccessToken { get; set; }
        public string? Email { get; set; }
        public string? UserId { get; set; }

        public AuthResponse(Supabase.Gotrue.Session? session)
        {
            AccessToken = session?.AccessToken;
            Email = session?.User?.Email;
            UserId = session?.User?.Id;
        }
    }
}
