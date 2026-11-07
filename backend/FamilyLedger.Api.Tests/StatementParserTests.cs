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
    [InlineData("hdfc-card-redacted.csv", "credit_card", "hdfc-card-csv", "matched")]
    [InlineData("icici-card-redacted.csv", "credit_card", "icici-card-csv", "matched")]
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
        Assert.Equal("2.2.0", result.ParserVersion);
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
    public async Task CardCycleVarianceIsReportedWhenIssuerTotalsDoNotMatchRows()
    {
        const string csv =
            "Previous Balance,1000.00\n" +
            "Purchases / Debits,1400.00\n" +
            "Payments / Credits,250.00\n" +
            "Total Amount Due,2150.00\n" +
            "Date,Transaction Description,Amount (in Rs.),Transaction Type,Reference\n" +
            "01/11/2026,ONLINE PURCHASE,1250.00,DR,CARD000000000001\n" +
            "02/11/2026,PURCHASE REVERSAL,250.00,CR,CARD000000000002\n";
        await using var stream = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(csv));
        var file = new FormFile(
            stream,
            0,
            stream.Length,
            "file",
            "card-cycle-variance.csv");
        var parser = new StatementParser(new RejectingOcrService());

        var result = await parser.ParseAsync(
            file,
            "credit_card",
            null,
            default);

        Assert.Equal("mismatch", result.Reconciliation.Status);
        Assert.Equal(150m, result.Reconciliation.Variance);
        Assert.Contains(
            "purchases/debits differ",
            result.Reconciliation.Message);
    }

    [Fact]
    public void CardCycleReconcilerSupportsPdfStyleSummaryLines()
    {
        var transactions = new[]
        {
            new StatementTransaction(
                DateTimeOffset.Parse("2026-11-01T00:00:00+05:30"),
                800m,
                "debit",
                "RESTAURANT",
                null,
                "CARD",
                "purchase",
                true),
            new StatementTransaction(
                DateTimeOffset.Parse("2026-11-03T00:00:00+05:30"),
                50m,
                "credit",
                "CASHBACK",
                null,
                "CARD",
                "refund",
                true)
        };

        var result = CardCycleReconciler.TryBuild(
        [
            "Previous Statement Balance INR 500.00",
            "Purchases and Other Charges INR 800.00",
            "Payments and Credits INR 50.00",
            "Total Amount Due (INR) 1,250.00"
        ],
        transactions);

        Assert.NotNull(result);
        Assert.Equal("matched", result.Status);
        Assert.Equal(1_250m, result.ClosingBalance);
        Assert.Equal(0m, result.Variance);
    }

    [Fact]
    public void CardCycleReconciliationRemainsUnavailableWithoutIssuerTotals()
    {
        var result = CardCycleReconciler.TryBuild(
        [
            "01/11/2026 RESTAURANT 800.00 DR"
        ],
        [
            new StatementTransaction(
                DateTimeOffset.Parse("2026-11-01T00:00:00+05:30"),
                800m,
                "debit",
                "RESTAURANT",
                null,
                "CARD",
                "purchase",
                true)
        ]);

        Assert.Null(result);
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
    public void GenericPdfMapperAssemblesMultiLineBankRows()
    {
        var result = GenericPdfTableMapper.Map(
        [
            "Opening Balance 10,000.00",
            "01/11/2026",
            "UPI SHOP UPI000000000001",
            "500.00",
            "9,500.00",
            "02/11/2026",
            "NEFT SALARY NEFT000000000002",
            "1,000.00",
            "10,500.00"
        ],
        "bank");

        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal("debit", result.Transactions[0].Direction);
        Assert.Equal(500m, result.Transactions[0].Amount);
        Assert.Equal("credit", result.Transactions[1].Direction);
        Assert.Equal(1_000m, result.Transactions[1].Amount);
        Assert.Equal("matched", result.Reconciliation.Status);
    }

    [Fact]
    public void GenericPdfMapperAppendsCardDirectionOnContinuationLine()
    {
        var result = GenericPdfTableMapper.Map(
        [
            "01/11/2026",
            "PAYMENT RECEIVED",
            "1,200.00",
            "CR"
        ],
        "credit_card");

        var transaction = Assert.Single(result.Transactions);
        Assert.Equal("credit", transaction.Direction);
        Assert.Equal(1_200m, transaction.Amount);
        Assert.True(transaction.NeedsReview);
    }

    [Fact]
    public void GenericPdfMapperPreservesLegitimateDuplicateCardRows()
    {
        var result = GenericPdfTableMapper.Map(
        [
            "01/11/2026 RESTAURANT 800.00 DR",
            "01/11/2026 RESTAURANT 800.00 DR"
        ],
        "credit_card");

        Assert.Equal(2, result.Transactions.Count);
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
