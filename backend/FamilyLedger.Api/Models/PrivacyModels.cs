namespace FamilyLedger.Api.Models;

public sealed class DeleteAccountRequest
{
    public string Confirmation { get; set; } = string.Empty;
}

public static class AccountDeletionPolicy
{
    public const string ConfirmationPhrase = "DELETE MY ACCOUNT";

    public static bool HasValidConfirmation(string? confirmation) =>
        string.Equals(confirmation?.Trim(), ConfirmationPhrase, StringComparison.Ordinal);
}
