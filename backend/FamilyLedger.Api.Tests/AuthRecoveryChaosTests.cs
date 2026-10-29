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
            RedirectTo = "https://finance.example/update-password"
        });

        Assert.IsType<OkObjectResult>(result);
        Assert.StartsWith("https://supabase.invalid/auth/v1/recover?", requestedUrl);
        Assert.Contains("redirect_to=", requestedUrl);
        Assert.DoesNotContain("/magiclink", requestedUrl);
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

    private static AuthController CreateController(
        HttpMessageHandler handler,
        bool includeConfiguration = true)
    {
        var values = includeConfiguration
            ? new Dictionary<string, string?>
            {
                ["Supabase:Url"] = "https://supabase.invalid",
                ["Supabase:AnonKey"] = "public-test-key"
            }
            : new Dictionary<string, string?>();

        var controller = new AuthController(
            new Supabase.Client(
                "https://supabase.invalid",
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
