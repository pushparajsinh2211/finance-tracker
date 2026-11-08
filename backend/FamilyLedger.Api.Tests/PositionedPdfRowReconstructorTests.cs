using FamilyLedger.Api.Services.FinanceHub;
using Xunit;

namespace FamilyLedger.Api.Tests;

public sealed class PositionedPdfRowReconstructorTests
{
    [Fact]
    public void UsesOrderedWithdrawalAndDepositHeadersForBlankColumns()
    {
        var rows = PositionedPdfRowReconstructor.ReconstructRows(
        [
            Word(1, "Withdrawal", 700, 780, 750),
            Word(1, "Deposit", 840, 900, 750),
            Word(1, "Balance", 940, 1_000, 750),
            Word(1, "Opening", 20, 80, 700),
            Word(1, "Balance", 90, 150, 700),
            Word(1, "10,000.00", 940, 1_000, 700),
            Word(1, "1", 20, 30, 650),
            Word(1, "03.11.2026", 50, 130, 650),
            Word(1, "03.11.2026", 150, 230, 650),
            Word(1, "UPI-PURCHASE", 260, 400, 650),
            Word(1, "500.00", 710, 770, 650),
            Word(1, "9,500.00", 940, 1_000, 650),
            Word(1, "2", 20, 30, 600),
            Word(1, "04.11.2026", 50, 130, 600),
            Word(1, "04.11.2026", 150, 230, 600),
            Word(1, "REFUND", 260, 340, 600),
            Word(1, "1,000.00", 840, 900, 600),
            Word(1, "10,500.00", 930, 1_000, 600)
        ],
        "bank");

        Assert.Contains(rows, row => row.EndsWith("DR"));
        Assert.Contains(rows, row => row.EndsWith("CR"));

        var result = GenericPdfTableMapper.Map(rows, "bank");
        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal("debit", result.Transactions[0].Direction);
        Assert.Equal("credit", result.Transactions[1].Direction);
        Assert.Equal("matched", result.Reconciliation.Status);
    }

    [Fact]
    public void DoesNotInferDirectionWithoutOrderedHeaders()
    {
        var rows = PositionedPdfRowReconstructor.ReconstructRows(
        [
            Word(1, "1", 20, 30, 650),
            Word(1, "03.11.2026", 50, 130, 650),
            Word(1, "PURCHASE", 260, 340, 650),
            Word(1, "500.00", 710, 770, 650),
            Word(1, "9,500.00", 940, 1_000, 650)
        ],
        "bank");

        Assert.DoesNotContain(rows, row => row.EndsWith("DR"));
        Assert.DoesNotContain(rows, row => row.EndsWith("CR"));
    }

    [Fact]
    public void KeepsPagesWithMatchingVerticalCoordinatesSeparate()
    {
        var rows = PositionedPdfRowReconstructor.ReconstructRows(
        [
            Word(1, "PAGE-ONE", 20, 100, 650),
            Word(2, "PAGE-TWO", 20, 100, 650)
        ],
        "bank");

        Assert.Equal(new[] { "PAGE-ONE", "PAGE-TWO" }, rows);
    }

    private static PositionedPdfWord Word(
        int page,
        string text,
        double left,
        double right,
        double bottom) =>
        new(page, text, left, right, bottom);
}
