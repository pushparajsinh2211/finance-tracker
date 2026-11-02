using FamilyLedger.Api.Models;
using Xunit;

namespace FamilyLedger.Api.Tests;

public sealed class AccountDeletionPolicyTests
{
    [Theory]
    [InlineData("DELETE MY ACCOUNT", true)]
    [InlineData(" DELETE MY ACCOUNT ", true)]
    [InlineData("delete my account", false)]
    [InlineData("DELETE ACCOUNT", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasValidConfirmation_RequiresExactPhrase(string? value, bool expected)
    {
        Assert.Equal(expected, AccountDeletionPolicy.HasValidConfirmation(value));
    }
}
