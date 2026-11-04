namespace FamilyLedger.Api.Services.FinanceHub;

public sealed record StatementProfile(string Id, string DisplayName);

public static class StatementProfileDetector
{
    public static StatementProfile Detect(IReadOnlyCollection<string> headers, string sourceType)
    {
        var values = headers.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (sourceType == "bank" &&
            values.Contains("chqrefno") &&
            values.Contains("withdrawalamt") &&
            values.Contains("depositamt"))
        {
            return new("hdfc-bank-csv", "HDFC Bank");
        }

        if (sourceType == "bank" &&
            values.Contains("refnochequeno") &&
            values.Contains("debit") &&
            values.Contains("credit"))
        {
            return new("sbi-bank-csv", "State Bank of India");
        }

        if (sourceType == "bank" &&
            values.Contains("transactionremarks") &&
            values.Contains("withdrawalamountinr") &&
            values.Contains("depositamountinr"))
        {
            return new("icici-bank-csv", "ICICI Bank");
        }

        if (sourceType == "credit_card" &&
            values.Contains("transactiondescription") &&
            (values.Contains("amountinrs") || values.Contains("amountinr")))
        {
            return new("hdfc-card-csv", "HDFC Credit Card");
        }

        if (sourceType == "credit_card" &&
            values.Contains("details") &&
            values.Contains("amountinr") &&
            values.Contains("drcr"))
        {
            return new("icici-card-csv", "ICICI Credit Card");
        }

        return new("generic-csv", "Generic CSV");
    }
}
