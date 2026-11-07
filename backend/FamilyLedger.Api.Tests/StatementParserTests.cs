using FamilyLedger.Api.Services.FinanceHub;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FamilyLedger.Api.Tests;

public sealed class StatementParserTests
{
    [Theory]
    [InlineData("hdfc-bank-redacted.csv", "bank", "hdfc-bank-csv", "matched")]
    [InlineData("sbi-bank-redacted.csv", "bank", "sbi-bank-csv", "matched")]
    [InlineData("icici-bank-redacted.csv", "bank", "icici-bank-csv", "matched")]
    [InlineData("hdfc-card-redacted.csv", "credit_card", "hdfc-card-csv", "unavailable")]
    [InlineData("icici-card-redacted.csv", "credit_card", "icici-card-csv", "unavailable")]
    public async Task RedactedInstitutionFixtureUsesExpectedProfileAndReconciliation(
        string fixture,
        string sourceType,
        string expectedParser,
        string expectedReconciliation)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        await using var stream = File.OpenRead(path);
        var file = new FormFile(stream, 0, stream.Length, "file", fixture);
        var parser = new StatementParser(new RejectingOcrService());

        var result = await parser.ParseAsync(file, sourceType, null, default);

        Assert.Equal(expectedParser, result.Parser);
        Assert.Equal("2.0.0", result.ParserVersion);
        Assert.Equal(2, result.Transactions.Count);
        Assert.Empty(result.Errors);
        Assert.Equal(expectedReconciliation, result.Reconciliation.Status);
        Assert.False(result.UsedOcr);
    }

    [Fact]
    public async Task BalanceVarianceIsReportedWithoutSilentlyBlockingPreview()
    {
        const string csv =
            "Date,Narration,Debit,Credit,Balance\n" +
            "01/11/2026,Purchase,100.00,,900.00\n" +
            "02/11/2026,Deposit,,50.00,999.00\n";
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var file = new FormFile(stream, 0, stream.Length, "file", "variance.csv");
        var parser = new StatementParser(new RejectingOcrService());

        var result = await parser.ParseAsync(file, "bank", null, default);

        Assert.Equal("mismatch", result.Reconciliation.Status);
        Assert.Equal(49m, result.Reconciliation.Variance);
        Assert.Contains("do not reconcile", result.Reconciliation.Message);
    }

    [Fact]
    public void UnknownHeadersFallBackToGenericProfile()
    {
        var profile = StatementProfileDetector.Detect(
            ["date", "description", "amount", "direction"],
            "bank");

        Assert.Equal("generic-csv", profile.Id);
    }


    [Fact]
    public void GenericPdfMapperUsesRunningBalanceToMapBankRows()
    {
        var result = GenericPdfTableMapper.Map(
        [
            "Opening Balance 10,000.00",
            "01/11/2026 UPI SHOP UPI000000000001 500.00 9,500.00",
            "02/11/2026 NEFT SALARY NEFT000000000002 1,000.00 10,500.00"
        ],
        "bank");

        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal("debit", result.Transactions[0].Direction);
        Assert.Equal(500m, result.Transactions[0].Amount);
        Assert.Equal("UPI", result.Transactions[0].PaymentRail);
        Assert.Equal("credit", result.Transactions[1].Direction);
        Assert.Equal(1_000m, result.Transactions[1].Amount);
        Assert.Equal("NEFT", result.Transactions[1].PaymentRail);
        Assert.All(result.Transactions, transaction => Assert.True(transaction.NeedsReview));
        Assert.Equal("matched", result.Reconciliation.Status);
        Assert.Equal(0m, result.Reconciliation.Variance);
        Assert.Equal(0, result.SkippedCandidateRows);
    }

    [Fact]
    public void GenericPdfMapperSkipsBankRowWhenDirectionCannotBeVerified()
    {
        var result = GenericPdfTableMapper.Map(
        [
            "01/11/2026 UNKNOWN TRANSFER 500.00"
        ],
        "bank");

        Assert.Empty(result.Transactions);
        Assert.Equal(1, result.SkippedCandidateRows);
        Assert.Equal("unavailable", result.Reconciliation.Status);
    }

    [Theory]
    [InlineData("01/11/2026 RESTAURANT 800.00 DR", "debit")]
    [InlineData("02/11/2026 PAYMENT RECEIVED 1,200.00 CR", "credit")]
    public void GenericPdfMapperUsesExplicitCardDirection(
        string row,
        string expectedDirection)
    {
        var result = GenericPdfTableMapper.Map([row], "credit_card");

        var transaction = Assert.Single(result.Transactions);
        Assert.Equal(expectedDirection, transaction.Direction);
        Assert.True(transaction.NeedsReview);
    }

    private sealed class RejectingOcrService : IStatementOcrService
    {
        public Task<string> RecognizePdfAsync(
            byte[] pdfBytes,
            string? password,
            CancellationToken cancellationToken) =>
            throw new Xunit.Sdk.XunitException("OCR must not run for CSV fixtures.");
    }
}
