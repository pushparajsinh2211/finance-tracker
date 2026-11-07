using System.Text;
using System.Text.Json;
using FamilyLedger.Api.Controllers;
using FamilyLedger.Api.Services.FinanceHub;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FamilyLedger.Api.Tests;

public sealed class FinanceHubImportPreviewTests
{
    [Fact]
    public async Task ZeroTransactionParseIsRejectedInsteadOfShowingCommitAction()
    {
        const string parserMessage =
            "No transaction rows could be recognized. This text layout needs a dedicated institution parser.";
        var parseResult = new StatementParseResult(
            "generic-text-pdf",
            "2.0.0",
            "pdf",
            new string('a', 64),
            [],
            [parserMessage],
            StatementReconciliation.Unavailable([], "Reconciliation unavailable."));

        var controller = new FinanceHubImportsController(
            new FixedParser(parseResult),
            new FixedHttpClientFactory(),
            new ConfigurationBuilder().Build(),
            new RejectingRetentionService());

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("synthetic"));
        var result = await controller.Preview(
            new StatementImportRequest
            {
                File = new FormFile(stream, 0, stream.Length, "file", "unsupported.pdf"),
                SourceType = "bank",
                AccountName = "Test account"
            },
            default);

        var rejected = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var body = JsonSerializer.Serialize(rejected.Value);
        Assert.Contains(parserMessage, body);
        Assert.Contains("generic-text-pdf", body);
    }

    private sealed class FixedParser(StatementParseResult result) : IStatementParser
    {
        public Task<StatementParseResult> ParseAsync(
            IFormFile file,
            string sourceType,
            string? password,
            CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class RejectingRetentionService : IStatementRetentionService
    {
        public Task<StatementRetentionResult> RetainAsync(
            IFormFile file,
            Guid userId,
            Guid importRunId,
            string checksum,
            int retentionDays,
            string authorization,
            CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("Retention must not run during preview.");

        public Task DeleteAsync(
            Guid importRunId,
            string authorization,
            CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("Deletion must not run during preview.");
    }
}
