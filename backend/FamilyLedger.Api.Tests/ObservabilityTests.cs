using System.Net;
using System.Text;
using FamilyLedger.Api.Health;
using FamilyLedger.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyLedger.Api.Tests;

public sealed class ObservabilityTests
{
    [Fact]
    public async Task CorrelationIdMiddlewarePreservesSafeClientIdentifier()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "mobile-request-123";

        var middleware = new CorrelationIdMiddleware(
            next: _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal("mobile-request-123", context.TraceIdentifier);
        Assert.Equal(
            "mobile-request-123",
            context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task CorrelationIdMiddlewareRejectsLogInjectionCharacters()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "unsafe\r\nvalue";

        var middleware = new CorrelationIdMiddleware(
            next: _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Matches("^[a-f0-9]{32}$", context.TraceIdentifier);
        Assert.DoesNotContain("\r", context.TraceIdentifier);
        Assert.DoesNotContain("\n", context.TraceIdentifier);
    }

    [Fact]
    public async Task ExceptionMiddlewareReturnsSanitizedErrorWithCorrelationId()
    {
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "request-456"
        };
        context.Response.Body = new MemoryStream();

        var middleware = new ApiExceptionMiddleware(
            next: _ => throw new InvalidOperationException("database password: private-secret"),
            NullLogger<ApiExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var response = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(
            "request-456",
            context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
        Assert.Contains("unexpected_error", response);
        Assert.Contains("request-456", response);
        Assert.DoesNotContain("private-secret", response);
    }

    [Fact]
    public async Task ReadinessFailsClosedWhenSupabaseConfigurationIsMissing()
    {
        var handler = new TrackingHandler();
        var healthCheck = new SupabaseHealthCheck(
            new ConfigurationBuilder().Build(),
            new FixedHttpClientFactory(new HttpClient(handler)));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task ReadinessChecksSupabaseAuthWithoutReadingResponseBody()
    {
        var handler = new TrackingHandler(HttpStatusCode.OK);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Supabase:Url"] = "https://supabase.example",
                ["Supabase:AnonKey"] = "public-anon-key"
            })
            .Build();
        var healthCheck = new SupabaseHealthCheck(
            configuration,
            new FixedHttpClientFactory(new HttpClient(handler)));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("https://supabase.example/auth/v1/health", handler.RequestedUri?.ToString());
        Assert.Equal("public-anon-key", handler.ApiKey);
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TrackingHandler(HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }
        public Uri? RequestedUri { get; private set; }
        public string? ApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            RequestedUri = request.RequestUri;
            ApiKey = request.Headers.TryGetValues("apikey", out var values)
                ? values.Single()
                : null;

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("should not be read")
            });
        }
    }
}
