using System.Net;
using System.Text;
using System.Text.Json;
using FamilyLedger.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FamilyLedger.Api.Tests;

public sealed class FinanceHubChaosTests
{
    [Fact]
    public async Task SupabaseNetworkOutageReturnsSanitizedBadGateway()
    {
        var controller = CreateController(new ChaosHandler(
            (_, _) => throw new HttpRequestException("connection refused: internal-host")));

        var result = await controller.ListTransactions(cancellationToken: default);

        AssertStatus(result, StatusCodes.Status502BadGateway, "temporarily unavailable");
        Assert.DoesNotContain("internal-host", JsonSerializer.Serialize(((ObjectResult)result).Value));
    }

    [Fact]
    public async Task SupabaseTimeoutReturnsSanitizedBadGateway()
    {
        var controller = CreateController(new ChaosHandler(
            (_, _) => throw new TaskCanceledException("upstream timeout with private detail")));

        var result = await controller.ListImports(cancellationToken: default);

        AssertStatus(result, StatusCodes.Status502BadGateway, "temporarily unavailable");
        Assert.DoesNotContain("private detail", JsonSerializer.Serialize(((ObjectResult)result).Value));
    }

    [Fact]
    public async Task MissingRuntimeConfigurationFailsClosed()
    {
        var controller = CreateController(
            new ChaosHandler((_, _) => throw new Xunit.Sdk.XunitException("HTTP must not be called")),
            includeConfiguration: false);

        var result = await controller.ListTransactions(cancellationToken: default);

        AssertStatus(result, StatusCodes.Status503ServiceUnavailable, "configuration is incomplete");
    }

    [Fact]
    public async Task UpstreamServerFailureDuringReviewIsGatewayFailure()
    {
        var controller = CreateController(new ChaosHandler(
            (_, _) => Task.FromResult(Response(
                HttpStatusCode.ServiceUnavailable,
                "database unavailable: do not expose"))));

        var result = await controller.ReviewTransaction(
            Guid.NewGuid(),
            new ReviewFinanceHubTransactionRequest { NeedsReview = false },
            default);

        AssertStatus(result, StatusCodes.Status502BadGateway, "could not be completed");
        Assert.DoesNotContain("database unavailable", JsonSerializer.Serialize(((ObjectResult)result).Value));
    }

    [Fact]
    public async Task UpstreamValidationFailureDuringReviewRemainsUnprocessable()
    {
        var controller = CreateController(new ChaosHandler(
            (_, _) => Task.FromResult(Response(HttpStatusCode.BadRequest, "invalid category"))));

        var result = await controller.ReviewTransaction(
            Guid.NewGuid(),
            new ReviewFinanceHubTransactionRequest { NeedsReview = false },
            default);

        AssertStatus(result, StatusCodes.Status422UnprocessableEntity, "could not be completed");
    }

    [Fact]
    public async Task ServiceRecoversAfterTransientSupabaseFailure()
    {
        var attempts = 0;
        var controller = CreateController(new ChaosHandler((_, _) =>
        {
            attempts++;
            return attempts == 1
                ? throw new HttpRequestException("temporary outage")
                : Task.FromResult(Response(HttpStatusCode.OK, "{\"items\":[],\"totalCount\":0}"));
        }));

        var failed = await controller.ListTransactions(cancellationToken: default);
        var recovered = await controller.ListTransactions(cancellationToken: default);

        AssertStatus(failed, StatusCodes.Status502BadGateway, "temporarily unavailable");
        var content = Assert.IsType<ContentResult>(recovered);
        Assert.Equal("application/json", content.ContentType);
        Assert.Contains("\"totalCount\":0", content.Content);
        Assert.Equal(2, attempts);
    }

    private static FinanceHubTransactionsController CreateController(
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

        var controller = new FinanceHubTransactionsController(
            new FixedHttpClientFactory(new HttpClient(handler)),
            new ConfigurationBuilder().AddInMemoryCollection(values).Build())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        controller.Request.Headers.Authorization = "Bearer chaos-test-token";
        return controller;
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string content) =>
        new(status)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private static void AssertStatus(IActionResult result, int expectedStatus, string expectedMessage)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        Assert.Contains(
            expectedMessage,
            JsonSerializer.Serialize(objectResult.Value),
            StringComparison.OrdinalIgnoreCase);
    }

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
