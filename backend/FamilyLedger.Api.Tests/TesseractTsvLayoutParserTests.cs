using FamilyLedger.Api.Services.FinanceHub;
using Xunit;

namespace FamilyLedger.Api.Tests;

public sealed class TesseractTsvLayoutParserTests
{
    [Fact]
    public void ReconstructRowsJoinsColumnsEvenWhenTesseractLineIdsDiffer()
    {
        var rows = TesseractTsvLayoutParser.ReconstructRows(
            Header +
            Word(1, 1, 10, 100, 90, 20, "01/11/2026") +
            Word(1, 2, 150, 102, 180, 18, "UPI-SHOP") +
            Word(1, 3, 700, 99, 80, 22, "500.00") +
            Word(1, 4, 850, 101, 100, 20, "9,500.00") +
            Word(1, 5, 10, 145, 90, 20, "02/11/2026") +
            Word(1, 6, 150, 147, 180, 18, "NEFT-SALARY") +
            Word(1, 7, 700, 144, 90, 22, "1,000.00") +
            Word(1, 8, 850, 146, 110, 20, "10,500.00"));

        Assert.Equal(2, rows.Count);
        Assert.Equal(
            "01/11/2026 UPI-SHOP 500.00 9,500.00",
            rows[0]);
        Assert.Equal(
            "02/11/2026 NEFT-SALARY 1,000.00 10,500.00",
            rows[1]);
    }

    [Fact]
    public void SpatialRowsFeedConservativeBankMapper()
    {
        var rows = TesseractTsvLayoutParser.ReconstructRows(
            Header +
            Word(1, 1, 10, 50, 80, 20, "Opening") +
            Word(1, 1, 100, 50, 80, 20, "Balance") +
            Word(1, 1, 850, 50, 100, 20, "10,000.00") +
            Word(1, 2, 10, 100, 90, 20, "01/11/2026") +
            Word(1, 3, 150, 102, 180, 18, "UPI-SHOP") +
            Word(1, 4, 700, 99, 80, 22, "500.00") +
            Word(1, 5, 850, 101, 100, 20, "9,500.00") +
            Word(1, 6, 10, 145, 90, 20, "02/11/2026") +
            Word(1, 7, 150, 147, 180, 18, "NEFT-SALARY") +
            Word(1, 8, 700, 144, 90, 22, "1,000.00") +
            Word(1, 9, 850, 146, 110, 20, "10,500.00"));

        var result = GenericPdfTableMapper.Map(rows, "bank");

        Assert.Equal(2, result.Transactions.Count);
        Assert.Equal("debit", result.Transactions[0].Direction);
        Assert.Equal("credit", result.Transactions[1].Direction);
        Assert.Equal("matched", result.Reconciliation.Status);
    }

    [Fact]
    public void ReconstructRowsKeepsPagesAndDistantLinesSeparate()
    {
        var rows = TesseractTsvLayoutParser.ReconstructRows(
            Header +
            Word(1, 1, 10, 100, 100, 20, "PAGE-ONE") +
            Word(1, 2, 10, 160, 100, 20, "NEXT-ROW") +
            Word(2, 1, 10, 100, 100, 20, "PAGE-TWO"));

        Assert.Equal(
            new[] { "PAGE-ONE", "NEXT-ROW", "PAGE-TWO" },
            rows);
    }

    [Fact]
    public void ReconstructRowsIgnoresNonWordAndMalformedRecords()
    {
        var rows = TesseractTsvLayoutParser.ReconstructRows(
            Header +
            "4\t1\t1\t1\t1\t0\t10\t10\t100\t20\t-1\t\n" +
            "not-a-tsv-row\n" +
            Word(1, 1, 10, 40, 100, 20, "VALID"));

        Assert.Equal(new[] { "VALID" }, rows);
    }

    private const string Header =
        "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n";

    private static string Word(
        int page,
        int line,
        int left,
        int top,
        int width,
        int height,
        string text) =>
        $"5\t{page}\t1\t1\t{line}\t1\t{left}\t{top}\t{width}\t{height}\t95.0\t{text}\n";
}
