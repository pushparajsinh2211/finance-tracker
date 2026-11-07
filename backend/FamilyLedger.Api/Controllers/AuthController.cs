using Microsoft.AspNetCore.Mvc;
using Supabase.Gotrue;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace FamilyLedger.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly Supabase.Client _supabaseClient;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public AuthController(
            Supabase.Client supabaseClient,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _supabaseClient = supabaseClient;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!MailAddress.TryCreate(request.Email, out _))
            {
                return BadRequest(new { Message = "Please provide a valid email address." });
            }

            if (!TryValidateRedirect(request.RedirectTo, out var redirectTo))
            {
                return BadRequest(new { Message = "The confirmation redirect URL is invalid." });
            }

            try
            {
                var options = string.IsNullOrWhiteSpace(redirectTo)
                    ? null
                    : new SignUpOptions { RedirectTo = redirectTo };

                var session = await _supabaseClient.Auth.SignUp(request.Email, request.Password, options);
                var requiresEmailConfirmation = string.IsNullOrEmpty(session?.AccessToken);

                var response = new AuthResponse(session)
                {
                    RequiresEmailConfirmation = requiresEmailConfirmation,
                    Message = requiresEmailConfirmation
                        ? "Account created. Check your inbox and confirm your email before signing in."
                        : "Account created and signed in."
                };

                if (requiresEmailConfirmation && !string.IsNullOrWhiteSpace(response.UserId))
                {
                    response.ConfirmationStatusToken =
                        CreateConfirmationStatusToken(response.UserId);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
        }

        [HttpPost("resend-confirmation")]
        public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequest request)
        {
            if (!MailAddress.TryCreate(request.Email, out _))
            {
                return BadRequest(new { Message = "Please provide a valid email address." });
            }

            if (!TryValidateRedirect(request.RedirectTo, out var redirectTo))
            {
                return BadRequest(new { Message = "The confirmation redirect URL is invalid." });
            }

            try
            {
                var supabaseUrl = _configuration["Supabase:Url"];
                var supabaseKey = _configuration["Supabase:AnonKey"];

                if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseKey))
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                    {
                        Message = "Authentication email delivery is not configured."
                    });
                }

                var client = _httpClientFactory.CreateClient();
                using var requestMessage = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{supabaseUrl.TrimEnd('/')}/auth/v1/resend");
                requestMessage.Headers.Add("apikey", supabaseKey);
                requestMessage.Content = JsonContent.Create(new
                {
                    type = "signup",
                    email = request.Email,
                    options = string.IsNullOrWhiteSpace(redirectTo)
                        ? null
                        : new { email_redirect_to = redirectTo }
                });

                using var response = await client.SendAsync(requestMessage);
                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, new
                    {
                        Message = "Confirmation email could not be sent. Please wait a minute before retrying, and verify SMTP and rate-limit settings."
                    });
                }

                return Ok(new
                {
                    Message = "If the account is awaiting confirmation, a new confirmation email has been sent."
                });
            }
            catch
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    Message = "Confirmation email could not be sent. Please try again shortly."
                });
            }
        }

        [HttpPost("confirmation-status")]
        public async Task<IActionResult> ConfirmationStatus(
            [FromBody] ConfirmationStatusRequest request)
        {
            if (!TryValidateConfirmationStatusToken(request.Token, out var userId))
            {
                return BadRequest(new
                {
                    Message = "The confirmation status request is invalid or expired."
                });
            }

            var supabaseUrl = _configuration["Supabase:Url"];
            var serviceRoleKey = _configuration["Supabase:ServiceRoleKey"];
            if (string.IsNullOrWhiteSpace(supabaseUrl) ||
                string.IsNullOrWhiteSpace(serviceRoleKey))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    Message = "Confirmation status is temporarily unavailable."
                });
            }

            try
            {
                using var requestMessage = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{supabaseUrl.TrimEnd('/')}/auth/v1/admin/users/{Uri.EscapeDataString(userId)}");
                requestMessage.Headers.Add("apikey", serviceRoleKey);
                requestMessage.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", serviceRoleKey);

                var client = _httpClientFactory.CreateClient();
                using var response = await client.SendAsync(requestMessage);
                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                    {
                        Message = "Confirmation status is temporarily unavailable."
                    });
                }

                await using var body = await response.Content.ReadAsStreamAsync();
                using var document = await JsonDocument.ParseAsync(body);
                var root = document.RootElement;
                var confirmed = HasTimestamp(root, "email_confirmed_at") ||
                                HasTimestamp(root, "confirmed_at");

                return Ok(new { Confirmed = confirmed });
            }
            catch
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    Message = "Confirmation status is temporarily unavailable."
                });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var session = await _supabaseClient.Auth.SignIn(request.Email, request.Password);
                if (session == null || string.IsNullOrEmpty(session.AccessToken))
                {
                    return Unauthorized(new { Message = "Login failed: no access token was returned. Confirm your email, then try again." });
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

            if (!TryValidateRedirect(request.RedirectTo, out var redirectTo))
            {
                return BadRequest(new { Message = "The password recovery redirect URL is invalid." });
            }

            var supabaseUrl = _configuration["Supabase:Url"];
            var supabaseKey = _configuration["Supabase:AnonKey"];
            if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseKey))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    Code = "email_service_unavailable",
                    Message = "Password reset email delivery is not configured."
                });
            }

            try
            {
                var endpoint = $"{supabaseUrl.TrimEnd('/')}/auth/v1/recover";
                if (!string.IsNullOrWhiteSpace(redirectTo))
                {
                    endpoint += $"?redirect_to={Uri.EscapeDataString(redirectTo)}";
                }

                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint);
                requestMessage.Headers.Add("apikey", supabaseKey);
                requestMessage.Content = JsonContent.Create(new { email = request.Email });

                var client = _httpClientFactory.CreateClient();
                using var response = await client.SendAsync(requestMessage);
                var responseBody = await response.Content.ReadAsStringAsync();
                var isRateLimited =
                    response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                    responseBody.Contains("over_email_send_rate_limit", StringComparison.OrdinalIgnoreCase) ||
                    responseBody.Contains("email rate limit exceeded", StringComparison.OrdinalIgnoreCase);

                if (isRateLimited)
                {
                    const int retryAfterSeconds = 60;
                    Response.Headers.RetryAfter = retryAfterSeconds.ToString();
                    return StatusCode(StatusCodes.Status429TooManyRequests, new
                    {
                        Code = "email_rate_limited",
                        Message = "The email service limit has been reached. Please wait before requesting another reset link.",
                        RetryAfterSeconds = retryAfterSeconds
                    });
                }

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode(StatusCodes.Status502BadGateway, new
                    {
                        Code = "email_service_unavailable",
                        Message = "Password reset email delivery is temporarily unavailable."
                    });
                }

                return Ok(new { Message = "If an account exists, a reset link has been sent." });
            }
            catch
            {
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    Code = "email_service_unavailable",
                    Message = "Password reset email delivery is temporarily unavailable."
                });
            }
        }

        private string? CreateConfirmationStatusToken(string userId)
        {
            var serviceRoleKey = _configuration["Supabase:ServiceRoleKey"];
            if (string.IsNullOrWhiteSpace(serviceRoleKey))
            {
                return null;
            }

            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
            var payload = $"{userId}|{expiresAt}";
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            var signature = HMACSHA256.HashData(
                DeriveConfirmationSigningKey(serviceRoleKey),
                payloadBytes);

            return $"{WebEncoders.Base64UrlEncode(payloadBytes)}.{WebEncoders.Base64UrlEncode(signature)}";
        }

        private bool TryValidateConfirmationStatusToken(
            string? token,
            out string userId)
        {
            userId = string.Empty;
            var serviceRoleKey = _configuration["Supabase:ServiceRoleKey"];
            if (string.IsNullOrWhiteSpace(serviceRoleKey) ||
                string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            try
            {
                var parts = token.Split('.', 2);
                if (parts.Length != 2)
                {
                    return false;
                }

                var payloadBytes = WebEncoders.Base64UrlDecode(parts[0]);
                var providedSignature = WebEncoders.Base64UrlDecode(parts[1]);
                var expectedSignature = HMACSHA256.HashData(
                    DeriveConfirmationSigningKey(serviceRoleKey),
                    payloadBytes);
                if (!CryptographicOperations.FixedTimeEquals(
                        providedSignature,
                        expectedSignature))
                {
                    return false;
                }

                var payload = Encoding.UTF8.GetString(payloadBytes).Split('|', 2);
                if (payload.Length != 2 ||
                    !Guid.TryParse(payload[0], out _) ||
                    !long.TryParse(payload[1], out var expiresAt) ||
                    expiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                {
                    return false;
                }

                userId = payload[0];
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static byte[] DeriveConfirmationSigningKey(string serviceRoleKey) =>
            SHA256.HashData(Encoding.UTF8.GetBytes(
                $"financehub-confirmation-status|{serviceRoleKey}"));

        private static bool HasTimestamp(JsonElement root, string propertyName) =>
            root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString());

        private static bool TryValidateRedirect(string? value, out string? redirectTo)
        {
            redirectTo = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var isSecure = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            var isLocalDevelopment =
                string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                uri.IsLoopback;

            if (!isSecure && !isLocalDevelopment)
            {
                return false;
            }

            // Preserve intentional UI state such as ?confirmed=1 while never forwarding fragments.
            redirectTo = uri.GetLeftPart(UriPartial.Query);
            return true;
        }
    }

    public class RegisterRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? RedirectTo { get; set; }
    }

    public class ResendConfirmationRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? RedirectTo { get; set; }
    }

    public class ConfirmationStatusRequest
    {
        public string Token { get; set; } = string.Empty;
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
        public bool RequiresEmailConfirmation { get; set; }
        public string? Message { get; set; }
        public string? ConfirmationStatusToken { get; set; }

        public AuthResponse(Supabase.Gotrue.Session? session)
        {
            AccessToken = session?.AccessToken;
            Email = session?.User?.Email;
            UserId = session?.User?.Id;
        }
    }
}
