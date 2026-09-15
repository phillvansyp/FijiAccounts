using FijiAccounts.Web.Services;
namespace FijiAccounts.Web.Tests;

public sealed class BankDocumentReferenceMatcherTests
{
    [Theory]
    [InlineData("92060241", "Rentokil INV92060241", null, true)]
    [InlineData("92060241", "Rentokil INV920602410", null, false)]
    [InlineData("92060241", "Rentokil INV92058716", null, false)]
    [InlineData("INV-000003", "Payment INV-000003", null, true)]
    [InlineData("92060241", "Payment", "92060241", true)]
    [InlineData("Rentokil", "Rentokil monthly fee", null, false)]
    [InlineData("", "Payment", null, false)]
    public void IdentifiesCompleteReferences(string reference, string description, string? bankReference, bool expected) =>
        Assert.Equal(expected, BankDocumentReferenceMatcher.Matches(reference, description, bankReference));
}
