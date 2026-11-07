using System.Net;
using System.Text;
using System.Text.Json;
using FamilyLedger.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FamilyLedger.Api.Tests;

public sealed class AuthRecoveryChaosTests
{
    [Fact]
    public async Task PasswordRecoveryUsesRecoverEndpointAndPreservesRedirect()
    {
        string? requestedUrl = null;
        var controller = CreateController(new ChaosHandler((request, _) =>
        {
            requestedUrl = request.RequestUri?.ToString();
            return Task.FromResult(Response(HttpStatusCode.OK, "{}"));
        }));

        var result = await controller.ResetPassword(new ResetPasswordRequest
        {
            Email = "person@example.test",
            RedirectTo = "https://finance.example/update-password?source=forgot#temporary-token"
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.StartsWith("https://supabase.invalid/auth/v1/recover?", requestedUrl);
        Assert.Contains("redirect_to=", requestedUrl);
        var encodedRedirect = requestedUrl?.Split("redirect_to=", 2)[1];
        Assert.Equal(
            "https://finance.example/update-password?source=forgot",
            Uri.UnescapeDataString(encodedRedirect ?? ""));
        Assert.DoesNotContain("temporary-token", requestedUrl);
        Assert.DoesNotContain("/magiclink", requestedUrl);
    }

    [Fact]
    public async Task ConfirmationResendPreservesQueryAndStripsFragment()
    {
        string? requestBody = null;
        var controller = CreateController(new ChaosHandler(async (request, _) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return Response(HttpStatusCode.OK, "{}");
        }));

        var result = await controller.ResendConfirmation(new ResendConfirmationRequest
        {
            Email = "person@example.test",
            RedirectTo = "https://finance.example/email-confirmed?registration=test-id#supabase-token"
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.Contains(
            "https://finance.example/email-confirmed?registration=test-id",
            requestBody);
        Assert.DoesNotContain("supabase-token", requestBody);
    }

    [Fact]
    public async Task EmailRateLimitReturnsStable429WithoutLeakingProviderBody()
    {
        var controller = CreateController(new ChaosHandler((_, _) =>
            Task.FromResult(Response(
                HttpStatusCode.TooManyRequests,
                "{\"code\":\"over_email_send_rate_limit\",\"msg\":\"SMTP private detail\"}"))));

        var result = await controller.ResetPassword(new ResetPasswordRequest
        {
            Email = "person@example.test"
        });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, objectResult.StatusCode);
        var body = JsonSerializer.Serialize(objectResult.Value);
        Assert.Contains("email_rate_limited", body);
        Assert.DoesNotContain("SMTP private detail", body);
        Assert.Equal("60", controller.Response.Headers.RetryAfter);
    }

    [Fact]
    public async Task ProviderFailureReturnsSanitizedBadGateway()
    {
        var controller = CreateController(new ChaosHandler((_, _) =>
            Task.FromResult(Response(
                HttpStatusCode.ServiceUnavailable,
                "{\"message\":\"internal SMTP hostname and credentials\"}"))));

        var result = await controller.ResetPassword(new ResetPasswordRequest
        {
            Email = "person@example.test"
        });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);
        var body = JsonSerializer.Serialize(objectResult.Value);
        Assert.Contains("email_service_unavailable", body);
        Assert.DoesNotContain("credentials", body);
    }

    [Fact]
    public async Task MissingEmailConfigurationFailsClosedWithoutCallingProvider()
    {
        var called = false;
        var controller = CreateController(
            new ChaosHandler((_, _) =>
            {
                called = true;
                return Task.FromResult(Response(HttpStatusCode.OK, "{}"));
            }),
            includeConfiguration: false);

        var result = await controller.ResetPassword(new ResetPasswordRequest
        {
            Email = "person@example.test"
        });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        Assert.False(called);
    }


    [Fact]
    public async Task ConfirmationStatusAcceptsSignedTokenAndReadsAdminUser()
    {
        string? requestedUrl = null;
        string? authorization = null;
        var controller = CreateController(new ChaosHandler((request, _) =>
        {
            requestedUrl = request.RequestUri?.ToString();
            authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(Response(
                HttpStatusCode.OK,
                "{\"email_confirmed_at\":\"2026-11-07T12:00:00Z\"}"));
        }));
        var userId = Guid.NewGuid().ToString();
        var tokenFactory = typeof(AuthController).GetMethod(
            "CreateConfirmationStatusToken",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        var token = Assert.IsType<string>(
            tokenFactory?.Invoke(controller, [userId]));

        var result = await controller.ConfirmationStatus(
            new ConfirmationStatusRequest { Token = token });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("\"confirmed\":true", JsonSerializer.Serialize(ok.Value));
        Assert.EndsWith($"/auth/v1/admin/users/{userId}", requestedUrl);
        Assert.Equal("Bearer private-service-role-test-key", authorization);
    }

    [Fact]
    public async Task ConfirmationStatusRejectsTamperedTokenWithoutCallingProvider()
    {
        var called = false;
        var controller = CreateController(new ChaosHandler((_, _) =>
        {
            called = true;
            return Task.FromResult(Response(HttpStatusCode.OK, "{}"));
        }));

        var result = await controller.ConfirmationStatus(
            new ConfirmationStatusRequest { Token = "tampered.token" });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(called);
    }

    private static AuthController CreateController(
        HttpMessageHandler handler,
        bool includeConfiguration = true)
    {
        var values = includeConfiguration
            ? new Dictionary<string, string?>
            {
                ["Supabase:Url"] = "https://supabase.invalid",
                ["Supabase:AnonKey"] = "public-test-key",
                ["Supabase:ServiceRoleKey"] = "private-service-role-test-key"
            }
            : new Dictionary<string, string?>();

        var controller = new AuthController(
            new Supabase.Client(
                "https://test-project.supabase.co",
                "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InRlc3QtcHJvamVjdCIsInJvbGUiOiJhbm9uIiwiaWF0IjoxNzAwMDAwMDAwLCJleHAiOjIwMDAwMDAwMDB9.test-signature",
                new Supabase.SupabaseOptions
                {
                    AutoConnectRealtime = false,
                    AutoRefreshToken = false
                }),
            new FixedHttpClientFactory(new HttpClient(handler)),
            new ConfigurationBuilder().AddInMemoryCollection(values).Build())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return controller;
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string content) =>
        new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ChaosHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> behavior)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            behavior(request, cancellationToken);
    }
}
